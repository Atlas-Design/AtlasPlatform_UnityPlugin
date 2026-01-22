![Atlas Unity Plugin Banner](Docs~/Images/Banner.png)

# Atlas Platform — Unity Workflow Plugin
A Unity **Editor plugin** that lets you load, configure, run, and inspect **Atlas Platform workflows** directly inside Unity.

This provides a full **workflow execution UI**, **job tracking**, and **result inspection**, designed to support iterative, production-style workflows.



> Status: **Early / active development**  
> APIs, schemas, and UI may evolve.

---

## What this plugin is

The Atlas Unity Workflow Editor is a lightweight **workflow orchestration layer** inside Unity.

It allows you to:

- Load workflow definitions from JSON
- Edit workflow inputs in a structured UI
- Execute workflows on the Atlas backend
- Track running jobs
- Inspect completed jobs and their outputs
- Import or reveal generated assets

Each workflow execution is tracked as a **Job**, with full input/output snapshots.

---
## Core concepts

### Workflow
A **Workflow** is a JSON definition describing:
- Inputs (boolean, number, string, image, mesh)
- Outputs (values or generated files)
- Execution metadata (API ID, base URL, version)

Workflows can be loaded from disk and stored in a local workflow library.

---

### Job
Each execution of a workflow creates a **Job**.

A job contains:
- A snapshot of input values at execution time
- Generated outputs
- Execution status (Running / Succeeded / Failed)
- Timestamps and duration

Jobs are **immutable** once completed.

---

### Live vs History

- **Live view**
  - Inputs are editable
  - Workflow can be executed
  - Outputs update after completion

- **History view**
  - Inputs and outputs are read-only
  - Generated files can be revealed or imported
  - Past runs remain fully inspectable

This separation prevents accidental state corruption and enables safe iteration.

---

## Features

- Workflow library management (load / switch workflows)
- Type-aware input UI:
  - Boolean, number, string
  - Image and mesh inputs from:
    - Project assets
    - External file paths
- Asset export support:
  - Texture2D → PNG
  - GameObject → GLB (via glTFast)
- Job tracking:
  - Running jobs panel
  - Job history with status and duration
- Output handling:
  - Image previews
  - Mesh import into project
  - File reveal for history jobs

---

## Requirements

- Unity **2021.3+** (recommended)
- Dependencies:
  - **Newtonsoft Json**
  - **glTFast** (required for GLB export)

---


## Install

### Option A — Install via Git URL (recommended)
In Unity: **Window → Package Manager → + → Add package from git URL…**

Paste: https://github.com/Atlas-Design/AtlasPlatform_UnityPlugin.git

### Option B — Install from disk (local dev)
- Clone or download this repo
- In Unity: **Package Manager → + → Add package from disk…**
- Select the plugin’s `package.json`

---
## Quick start

1. Open the editor window  
   **Window → Atlas Workflow**

2. Configure settings  
   **Edit → Project Settings → Atlas Workflow**  
   Set the **Asset Save Folder** (must be inside `Assets/`).

3. Load a workflow
   - Click **Load Workflow**
   - Select a workflow JSON file

4. Set inputs
   - Primitive values (bool / number / string)
   - Assets (textures / meshes) or external files

5. Run the workflow
   - A new Job is created
   - Progress appears in the Running Jobs panel
   - Results populate when complete

6. Inspect results
   - View outputs in the live panel
   - Browse previous runs in Job History
   - Import or reveal generated assets

---


## Where files go
h
- **Temporary exports & downloads**  
  OS temp directory under:  UnityAtlasWorkflow/

- **Job history**  
Stored next to the Unity project (outside `Assets/`): AtlasWorkflowJobs/

Each job folder contains:
- Input files
- Output files
- A serialized job snapshot

## License

MIT — see the `LICENSE` file.

---

## Notes

- This plugin is Editor-focused
- A reachable Atlas backend is required to execute workflows
- Workflow schemas and APIs may change as the platform evolves


---