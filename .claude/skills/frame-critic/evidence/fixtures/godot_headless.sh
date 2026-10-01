#!/bin/bash
# Proof-only: Godot headless (no window, no focus, muted) for the builder-throw case.
exec /Applications/Godot_mono.app/Contents/MacOS/Godot --headless --audio-driver Dummy "$@"
