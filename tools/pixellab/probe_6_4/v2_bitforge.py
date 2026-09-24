#!/usr/bin/env python3
"""The §6.4 probe's FROZEN generation surface: v2 HTTP BitForge.

Frozen for the entire probe — every arm, every seat, every stage — on the evidence in
`AUDIT-FINDINGS.md`: v2 BitForge is the only surface where a 24-32px native canvas and a
shading lever coexist AND the lever is measurable (pixdiff 1.0000 against a measured noise
floor of 0.3542; MCP `pro` and `pixflux` both measure 1.0000 against a 1.0000 noise floor,
which is NO INSTRUMENT).

Deliberately NOT built on `client_compat.py`:

  * `client_compat` is v1 and carries a LEGACY banner.
  * `client_compat.generate_image_bitforge` passes `style_image` through raw kwargs, where it
    dies as `TypeError: Object of type Image is not JSON serializable`. **The wrapper has never
    been able to carry a style image** (AUDIT-FINDINGS, column 1). Stage 2 of this probe is
    entirely style-conditioned, so routing through it is a guaranteed failure. Filed as its own
    defect; this module encodes `Base64Image` by hand.

THE LEDGER STORES IMAGES, NOT PARAMETERS. No surface on this platform is seed-reproducible —
an eight-sample census returned 3 distinct outputs from 8 identical calls (AUDIT-FINDINGS,
column 2). An image is therefore NOT re-derivable from (prompt, seed), and any ledger that
records only parameters has lost the evidence. Every generation — accepted, rejected, or
control — is written to disk beside a ledger row carrying its full request payload.
"""
import base64
import copy
import hashlib
import io
import json
import os
import subprocess
import time

import requests
from PIL import Image

V2_BASE = "https://api.pixellab.ai/v2"
ENDPOINT = "/create-image-bitforge"
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))

_TOKEN_VARS = ("PIXELLAB_API_TOKEN", "PIXELLAB_API_KEY")


def api_token():
    for var in _TOKEN_VARS:
        val = os.environ.get(var)
        if val:
            return val
    # The credential lives in ~/.zshrc, which a tool shell never sources. Read the export line
    # ourselves rather than require `zsh -ic` around every call; the value is returned, never
    # printed or logged (the ledger redacts payloads and carries no headers).
    import re
    for rc in ("~/.zshenv", "~/.zshrc"):
        path = os.path.expanduser(rc)
        if not os.path.exists(path):
            continue
        for line in open(path):
            m = re.match(r"\s*(?:export\s+)?(PIXELLAB_API_TOKEN|PIXELLAB_API_KEY)=[\"']?([^\"'\s]+)", line)
            if m:
                return m.group(2)
    raise RuntimeError("No PixelLab credential. Set one of: " + ", ".join(_TOKEN_VARS))


def _headers():
    return {"Authorization": "Bearer %s" % api_token(),
            "Content-Type": "application/json"}


def git_commit():
    r = subprocess.run(["git", "-C", REPO, "rev-parse", "HEAD"],
                       capture_output=True, text=True)
    return r.stdout.strip() or "UNKNOWN"


# --- refusal capture ---------------------------------------------------------
# A refusal's reason is unrecoverable after the fact. requests' raise_for_status() reports
# only the status line and drops r.text, where the server's actual reason lives. Same
# discipline as client_compat._check, reimplemented here so the v2 path does not depend on
# the legacy module for anything.

class Refusal(RuntimeError):
    def __init__(self, status, classification, reason, payload):
        self.status = status
        self.classification = classification
        self.reason = reason
        self.payload = payload
        super().__init__("HTTP %s [%s] %s" % (status, classification, reason[:400]))


def _classify(status, body):
    b = body.lower()
    if status == 423:
        # AUDIT: 423 means "still generating", not "refused". Never file it as an error.
        return "in_progress"
    if status == 401 or status == 403:
        return "auth"
    if status == 402 or "insufficient" in b:
        return "insufficient_balance"
    if status == 429:
        return "rate_limit"
    if "string_too_long" in b or "at most" in b:
        return "over_length"
    if "content" in b and "policy" in b:
        return "content_policy"
    if status == 422 or status == 400:
        return "invalid_input"
    if status >= 500:
        return "server_error"
    return "unknown"


def _check(r, payload):
    if r.status_code == 200:
        return
    body = ""
    try:
        body = r.text
    except Exception:
        body = "<body unreadable>"
    raise Refusal(r.status_code, _classify(r.status_code, body), body, payload)


def enc(img):
    """PIL -> the Base64Image wire shape. Hand-encoded; see the module docstring."""
    buf = io.BytesIO()
    img.save(buf, format="PNG")
    return {"type": "base64",
            "base64": base64.b64encode(buf.getvalue()).decode("ascii"),
            "format": "png"}


def _redact(payload):
    """Ledger-safe copy: image fields become a digest + size, never megabytes of base64."""
    p = copy.deepcopy(payload)
    for field in ("style_image", "init_image", "color_image", "inpainting_image", "mask_image"):
        v = p.get(field)
        if isinstance(v, dict) and v.get("base64"):
            raw = base64.b64decode(v["base64"])
            im = Image.open(io.BytesIO(raw))
            p[field] = {"_ref": "sha256:" + hashlib.sha256(raw).hexdigest(),
                        "_size": list(im.size), "_bytes": len(raw)}
    return p


# --- balance -----------------------------------------------------------------

def balance():
    """The whole /balance object. AUDIT 8.1: record the whole response, not one number —
    dollar credits and the subscription pool arrive together."""
    r = requests.get(V2_BASE + "/balance", headers=_headers(), timeout=60)
    r.raise_for_status()
    return r.json()


def pool(bal):
    try:
        return float(bal["subscription"]["generations"])
    except (KeyError, TypeError, ValueError):
        return None


def settled_pool(reads=3, interval=10, tries=8):
    """AUDIT 8.5: the balance ledger settles LATE and SLOWLY — two reads 15s apart agreed on a
    figure that was 32% low. A cost taken from an unsettled bracket is a LOWER BOUND, not a
    measurement. Require `reads` consecutive identical values before believing one."""
    seen = []
    for _ in range(tries):
        v = pool(balance())
        seen.append(v)
        if len(seen) >= reads and len(set(seen[-reads:])) == 1 and seen[-1] is not None:
            return seen[-1], True
        time.sleep(interval)
    return seen[-1] if seen else None, False


# --- the generation call -----------------------------------------------------

class Ledger:
    """One flushed row per call, plus the image on disk. Flushed per row because a crash
    mid-batch must not lose the record of calls that were already billed."""

    def __init__(self, out_dir, name="ledger.jsonl"):
        self.dir = out_dir
        os.makedirs(out_dir, exist_ok=True)
        self.path = os.path.join(out_dir, name)
        self.commit = git_commit()

    def write(self, row):
        row = dict(row)
        row.setdefault("commit", self.commit)
        row.setdefault("surface", "v2-http" + ENDPOINT)
        with open(self.path, "a") as f:
            f.write(json.dumps(row, sort_keys=True) + "\n")
            f.flush()
            os.fsync(f.fileno())
        return row


def generate(payload, ledger, image_name, image_subdir="images", claim="", extra=None):
    """One BitForge generation. Returns (PIL image or None, ledger row).

    A refusal is ledgered exactly as loudly as a success — it is a measured fact about the
    surface, and the whole point of capturing r.text before raising.

    `extra` is merged into the row BEFORE it is written, so caller-side facts (arm, subject,
    seat) land in the ledger itself rather than only in the returned copy. Evidence that
    exists only in memory is not evidence.
    """
    t0 = time.time()
    img = None
    row = {"claim": claim, "image": None, "request": _redact(payload)}
    if extra:
        row.update(extra)
    try:
        r = requests.post(V2_BASE + ENDPOINT, headers=_headers(), json=payload, timeout=600)
        _check(r, payload)
        j = r.json()
        b64 = j["image"]["base64"]
        if "," in b64[:64] and b64.lstrip().startswith("data:"):
            b64 = b64.split(",", 1)[1]
        raw = base64.b64decode(b64)
        img = Image.open(io.BytesIO(raw)).convert("RGBA")

        d = os.path.join(ledger.dir, image_subdir)
        os.makedirs(d, exist_ok=True)
        rel = os.path.join(image_subdir, image_name + ".png")
        img.save(os.path.join(ledger.dir, rel))

        row.update(verdict="OK", image=rel,
                   image_sha256=hashlib.sha256(raw).hexdigest(),
                   out_size=list(img.size),
                   usage=j.get("usage"),
                   seconds=round(time.time() - t0, 1))
    except Refusal as e:
        row.update(verdict="REFUSED:" + e.classification, http_status=e.status,
                   reason=e.reason[:2000], seconds=round(time.time() - t0, 1))
    except Exception as e:  # network, decode, disk
        row.update(verdict="ERROR:" + type(e).__name__, reason=str(e)[:2000],
                   seconds=round(time.time() - t0, 1))
    return img, ledger.write(row)
