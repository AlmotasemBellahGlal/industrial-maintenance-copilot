"""Opt-in loopback demo role proof; never prints credentials or user/model content."""
import json, pathlib, urllib.request, urllib.error
ROOT = pathlib.Path(__file__).resolve().parents[2]
BASE = "http://127.0.0.1:5000/api"
def token(name): return (ROOT / "artifacts/issue25" / name).read_text().strip()
def request(path, credential, body=None):
    req = urllib.request.Request(BASE + path, data=None if body is None else json.dumps(body).encode(), headers={"Authorization":"Bearer " + credential,"Content-Type":"application/json"})
    try:
        with urllib.request.urlopen(req, timeout=90) as response: return response.status, json.load(response)
    except urllib.error.HTTPError as error: return error.code, {}
tech, supervisor = token("technician-credential.txt"), token("credential.txt")
status, workflow = request("/runs", tech, {"equipmentId":"11111111-1111-1111-1111-111111111111","symptom":"pump vibration and seal leakage"})
assert status == 201, status
path = "/work-orders/" + workflow["workOrderId"]
status, review = request(path, tech)
assert status == 200
assert request(path + "/decisions", tech, {"target":review["target"],"decision":"Approve"})[0] == 403
assert request(path + "/dispatch", tech, review["target"])[0] == 403
assert request(path + "/verifications", tech, {"target":review["target"],"prerequisiteId":review["requirements"][0]["id"],"evidence":"synthetic demo observation","satisfied":True})[0] == 403
assert request(path + "/decisions", supervisor, {"target":review["target"],"decision":"Approve"})[0] == 200
_, approved = request(path, supervisor)
assert request(path + "/dispatch", supervisor, approved["target"])[0] == 422
print("PASS: technician can diagnose/read but cannot approve/verify/dispatch; supervisor can approve; approval alone cannot bypass safety verification.")
