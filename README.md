# SymFSM API

## From Text Generation to Computable Reasoning

SymFSM is a cognitive reasoning architecture that adds a formal reasoning layer on top of Large Language Models.

Instead of generating answers directly from prompts, SymFSM builds and analyzes a task structure before generation.

The goal is not to make models produce more text.

The goal is to make reasoning itself a computable object.

---

## How SymFSM Works

Traditional LLM workflow:

```text
Prompt
   ↓
LLM
   ↓
Answer
```

SymFSM workflow:

```text
Prompt
   ↓
Cognitive Map
   ↓
Reachability Analysis
   ↓
Structural Repair
   ↓
LLM Generation
   ↓
Verification
   ↓
Answer
```

Before text generation, SymFSM attempts to determine:

* what concepts exist in the task;
* how they are connected;
* whether the goal is reachable;
* where reasoning gaps exist;
* which solution trajectories are valid.

---

## Key Features

### Cognitive Maps

Tasks are transformed into structured cognitive graphs.

Graph nodes may represent:

* concepts;
* goals;
* constraints;
* mechanisms;
* hypotheses;
* dependencies.

---

### Reachability Analysis

SymFSM evaluates whether a target conclusion can be reached from known information.

Instead of guessing missing steps, the system detects reasoning gaps.

---

### Structural Repair

When the reasoning graph is incomplete, SymFSM can:

* introduce missing concepts;
* create sub-maps;
* reorganize dependencies;
* search alternative reasoning paths.

---

### Finite State Reasoning

The reasoning process is controlled by finite-state machines.

This prevents arbitrary jumps between reasoning stages and enables formal analysis of solution trajectories.

---

### Recursive Reasoning

During answer generation, the model may request:

* expansion of a local cognitive map;
* creation of a new sub-map;
* reachability verification;
* structural repair.

Reasoning can therefore evolve dynamically while the answer is being generated.

---

### Experience Accumulation

SymFSM stores:

* successful reasoning trajectories;
* cognitive patterns;
* repair strategies;
* structural solution templates.

The system learns solution strategies rather than memorizing answers.

---

## Typical Use Cases

* Product strategy
* Business architecture
* Research tasks
* Engineering design
* Complex planning
* Security analysis
* Malware analysis
* Knowledge-intensive decision support

---

# API

## Submit Task

Endpoint:

```http
POST /submit
```

Request:

```json
{
  "prompt": "Generate 10 business ideas for the artificial intelligence industry"
}
```

Response:

```json
{
  "id": "task_id",
  "status": "queued"
}
```

---

## Get Result

Endpoint:

```http
GET /result?id=task_id
```

Response:

```json
{
  "id": "task_id",
  "status": "done",
  "result": "..."
}
```

Possible statuses:

```text
queued
running
done
error
```

---

# Server Address

Current API endpoint:

```text
http://ip:8088
```

Example:

```text
http://ip:8088/submit
```

Future versions may use a domain name instead of a direct IP address.

---

# C# Example

See:

```text
examples/Program.cs
```

---

# Python Example

See:

```text
examples/example.py
```

---

# Website

https://principium.pro/symfsm/

---

# License

Research Prototype

Copyright © SymFSM
