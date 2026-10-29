"""Publish an existing draft only when its successful build and reviewed assets match."""
import json
import os
import re
import subprocess
import sys

with open(sys.argv[1], encoding="utf-8-sig") as source:
    manifest = json.load(source)
repository = os.environ["GITHUB_REPOSITORY"]
tag, commit, run_id = manifest["tag"], manifest["commit"], manifest["run_id"]
release_id = manifest["release_id"]
assert re.fullmatch(r"v\d+\.\d+\.\d+", tag), "Invalid release tag"
assert re.fullmatch(r"[0-9a-f]{40}", commit), "Invalid commit"
assert isinstance(run_id, int) and run_id > 0, "Invalid build ID"
assert isinstance(release_id, int) and release_id > 0, "Invalid release ID"


def api(path, allow_missing=False):
    result = subprocess.run(["gh", "api", f"repos/{repository}/{path}"],
                            capture_output=True, text=True)
    if allow_missing and result.returncode != 0 and "HTTP 404" in result.stderr:
        return None
    result.check_returncode()
    return json.loads(result.stdout)


run = api(f"actions/runs/{run_id}")
assert run["status"] == "completed" and run["conclusion"] == "success", "Build has not passed"
assert run["head_sha"] == commit and run["head_branch"] == "main", "Build commit differs"
assert run["path"] == ".github/workflows/release.yml", "Unexpected build workflow"
release = api(f"releases/{release_id}")
assert release["tag_name"] == tag, "Unexpected release"
ref = api(f"git/ref/tags/{tag}", allow_missing=True)
if ref is None:
    # GitHub creates a new draft's tag when the release is published.
    assert release["draft"] and release["target_commitish"] == commit, "Draft target differs from tested source"
else:
    assert ref["object"]["type"] == "commit" and ref["object"]["sha"] == commit, "Release tag differs from tested source"
expected_names = {"SHA256SUMS.txt"}
for runtime in ("win-x64", "osx-arm64", "osx-x64"):
    expected_names.add(f"YtDlpGui-{runtime}.zip")
    expected_names.add(f"YtDlpGui-sources-{runtime}.tar.gz")
reviewed = manifest["assets"]
actual = {asset["name"]: asset for asset in release["assets"]}
assert set(reviewed) == expected_names and set(actual) == expected_names, "Release assets are incomplete"
for name, asset in actual.items():
    assert asset["state"] == "uploaded" and asset["size"] > 0, f"Asset is not ready: {name}"
    assert reviewed[name]["size"] == asset["size"], f"Asset size changed: {name}"
    assert re.fullmatch(r"sha256:[0-9a-f]{64}", reviewed[name]["digest"]), f"Invalid digest: {name}"
    assert reviewed[name]["digest"] == asset["digest"], f"Asset digest changed: {name}"
if release["draft"]:
    subprocess.run(["gh", "release", "edit", tag, "--repo", repository,
                    "--draft=false", "--latest"], check=True)
published = api(f"releases/{release_id}")
assert not published["draft"], "Release is still a draft"
ref = api(f"git/ref/tags/{tag}")["object"]
assert ref["type"] == "commit" and ref["sha"] == commit, "Published tag differs from tested source"
print(published["html_url"])
