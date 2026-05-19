#!/usr/bin/env python3

import json
import os
from pathlib import Path


def main() -> None:
    version = os.environ["RELEASE_VERSION"]
    channel = os.environ["RELEASE_CHANNEL"]
    metadata_path = Path("Releases") / f"releases.{channel}.json"

    payload = json.loads(metadata_path.read_text())
    payload["Assets"] = [
        asset for asset in payload.get("Assets", [])
        if asset.get("Version") == version
    ]

    metadata_path.write_text(json.dumps(payload, separators=(",", ":")))


if __name__ == "__main__":
    main()
