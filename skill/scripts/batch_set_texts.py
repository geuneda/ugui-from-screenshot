#!/usr/bin/env python3
"""
TMP 텍스트 일괄 설정 스크립트.

배경: unity-cli ui text.create에 text="..."를 전달하면 bash가 공백 기준으로
쪼개서 첫 단어만 전달되는 경우가 많다. 생성 시엔 빈 값 또는 임시값을 두고,
생성 후 이 스크립트로 JSON을 통해 정확히 설정한다.

Usage:
    편집해서 texts dict에 이름/텍스트 채운 뒤 실행.
    python3 batch_set_texts.py

주의: TMP 속성명은 text (m_text 아님).
"""

import subprocess
import json
import sys


def set_tmp_text(name: str, text: str) -> bool:
    """TextMeshProUGUI의 text 속성을 설정한다."""
    values = json.dumps({"text": text}, ensure_ascii=False)
    result = subprocess.run(
        ["unity-cli", "component", "update",
         f"name={name}",
         "type=TMPro.TextMeshProUGUI",
         f"values={values}"],
        capture_output=True, text=True
    )
    return result.returncode == 0


def set_legacy_text(name: str, text: str) -> bool:
    """UnityEngine.UI.Text의 text 속성을 설정한다 (legacy)."""
    values = json.dumps({"text": text}, ensure_ascii=False)
    result = subprocess.run(
        ["unity-cli", "component", "update",
         f"name={name}",
         "type=UnityEngine.UI.Text",
         f"values={values}"],
        capture_output=True, text=True
    )
    return result.returncode == 0


def verify_texts(expected: dict) -> list:
    """현재 씬의 TMP 텍스트 값과 expected 비교, 불일치 목록 반환."""
    result = subprocess.run(
        ["unity-cli", "resource", "get", "ui/hierarchy"],
        capture_output=True, text=True
    )
    if result.returncode != 0:
        return list(expected.keys())
    data = json.loads(result.stdout)
    by_name = {i["name"]: i.get("text") for i in data["data"]["items"]}
    mismatched = []
    for name, want in expected.items():
        got = by_name.get(name)
        if got != want:
            mismatched.append((name, want, got))
    return mismatched


if __name__ == "__main__":
    # 실제 사용 시 이 dict를 편집하거나 argv/stdin으로 받도록 바꾼다
    texts = {
        # "HeadingTitle": "MCP Import Smoke",
        # "HeadingSubtitle": "Figma MCP로 생성 후 ...",
    }
    if not texts:
        print("No texts configured. Edit this script's 'texts' dict.", file=sys.stderr)
        sys.exit(1)

    for name, text in texts.items():
        ok = set_tmp_text(name, text)
        print(f"{'OK' if ok else 'FAIL'} {name}")

    mismatched = verify_texts(texts)
    if mismatched:
        print("\n=== Mismatches ===")
        for name, want, got in mismatched:
            print(f"{name}: want={want!r} got={got!r}")
    else:
        print("\nAll texts verified.")
