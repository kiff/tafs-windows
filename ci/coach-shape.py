"""The Coach panel's "Taking shape" screenshot (--page coach-wide): tafs cloud's contracts, planque's grants and KIFF's
draft beside them (tests/fixtures/coach-shape), and the conversation's approval naming that draft, as one does once KIFF
has drafted the Card (the contract's names none). Usage: python ci/coach-shape.py <folder>."""
import json
import pathlib
import shutil
import sys

out = pathlib.Path(sys.argv[1])
out.mkdir(parents=True, exist_ok=True)
for source in ("shared/contracts", "tests/fixtures/coach-shape"):
    for f in pathlib.Path(source).glob("*.json"):
        shutil.copy(f, out)

conversation = out / "coach-conversation.json"
answer = json.loads(conversation.read_text(encoding="utf-8"))


def name_draft(node):
    if isinstance(node, dict):
        if node.get("type") == "approval":
            node["draft"] = "drf_s1"
        for value in node.values():
            name_draft(value)
    elif isinstance(node, list):
        for value in node:
            name_draft(value)


name_draft(answer)
conversation.write_text(json.dumps(answer, ensure_ascii=False, indent=2), encoding="utf-8")
