import contextlib
import copy
import io
import json
import os
import pathlib
import runpy
import subprocess
import tempfile
import unittest
from unittest.mock import patch


class PublicationTests(unittest.TestCase):
    def exercise(self, mutation=None):
        commit = "a" * 40
        digest = "sha256:" + "b" * 64
        names = ["SHA256SUMS.txt"]
        for runtime in ("win-x64", "osx-arm64", "osx-x64"):
            names += [f"YtDlpGui-{runtime}.zip", f"YtDlpGui-sources-{runtime}.tar.gz"]
        manifest = dict(tag="v1.1.0", commit=commit, run_id=1, release_id=2,
                        assets={name: dict(size=100, digest=digest) for name in names})
        run = dict(status="completed", conclusion="success", head_sha=commit,
                   head_branch="main", path=".github/workflows/release.yml")
        release = dict(tag_name="v1.1.0", draft=True, target_commitish=commit,
                       html_url="https://github.com/example/repo/releases/tag/v1.1.0",
                       assets=[dict(name=name, size=100, digest=digest, state="uploaded") for name in names])
        if mutation:
            mutation(run, release)
        published = []

        def gh(command, **kwargs):
            if command[1:3] == ["release", "edit"]:
                published.append(command)
                release["draft"] = False
                return subprocess.CompletedProcess(command, 0)
            path = command[2]
            if "/actions/runs/" in path:
                data = run
            elif "/git/ref/" in path:
                if release["draft"]:
                    return subprocess.CompletedProcess(command, 1, "", "gh: Not Found (HTTP 404)")
                data = {"object": {"type": "commit", "sha": commit}}
            else:
                data = copy.deepcopy(release)
            return subprocess.CompletedProcess(command, 0, json.dumps(data), "")

        with tempfile.TemporaryDirectory() as temporary:
            manifest_path = pathlib.Path(temporary) / "publish.json"
            manifest_path.write_text(json.dumps(manifest), encoding="utf-8")
            script = pathlib.Path(__file__).resolve().parents[1] / "scripts/publish-release.py"
            with patch.dict(os.environ, GITHUB_REPOSITORY="example/repo"), \
                    patch("sys.argv", [str(script), str(manifest_path)]), \
                    patch("subprocess.run", side_effect=gh), contextlib.redirect_stdout(io.StringIO()):
                try:
                    runpy.run_path(str(script), run_name="__main__")
                except AssertionError:
                    self.assertEqual(published, [], "Rejected assets were published")
                    return False
        return bool(published)

    def test_verified_draft_creates_the_tested_tag_and_publishes(self):
        self.assertTrue(self.exercise())

    def test_failed_build_is_rejected(self):
        self.assertFalse(self.exercise(lambda run, _: run.update(conclusion="failure")))

    def test_changed_asset_is_rejected(self):
        self.assertFalse(self.exercise(lambda _, release: release["assets"][0].update(digest="sha256:" + "c" * 64)))

    def test_missing_platform_package_is_rejected(self):
        self.assertFalse(self.exercise(lambda _, release: release["assets"].pop()))

    def test_wrong_draft_source_is_rejected(self):
        self.assertFalse(self.exercise(lambda _, release: release.update(target_commitish="c" * 40)))


if __name__ == "__main__":
    unittest.main()
