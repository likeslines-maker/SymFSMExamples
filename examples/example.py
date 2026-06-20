import requests
import time

API_IP = "ip SymFSM Server - https://principium.pro/symfsm/"
BASE_URL = f"http://{API_IP}:8088"

prompt = "Generate 10 business ideas for the artificial intelligence industry"

print("Submitting task...")

response = requests.post(
f"{BASE_URL}/submit",
json={
"prompt": prompt
}
)

data = response.json()

task_id = data.get("id")

if not task_id:
print("Failed to get task id")
exit()

print("Task ID:", task_id)

while True:
time.sleep(30)

```
result = requests.get(
    f"{BASE_URL}/result",
    params={
        "id": task_id
    }
)

data = result.json()

status = data.get("status")

print("Status:", status)

if status == "done":
    print("\nRESULT:\n")
    print(data.get("result"))
    break

if status == "error":
    print("\nERROR:\n")
    print(data.get("error"))
    break
```
