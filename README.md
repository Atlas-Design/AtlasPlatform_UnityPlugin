# Atlas Platform — Unity Workflow Plugin
![Atlas Unity Plugin Banner](Docs~/Images/Banner.png)
Unity Editor plugin for running **Atlas Platform workflows** directly from Unity: load a workflow JSON, provide inputs (values / images / meshes), execute on the Atlas backend, and download outputs back to your machine.

> Status: **Early / in active development** (API + UI are evolving)

---

## What this plugin does

- Load a workflow definition (`.json`) and populate an editable workflow state (inputs/outputs, metadata).
- Upload file-based inputs (images/meshes), execute the workflow, and retrieve results.
- Export Unity assets when needed:
  - Texture2D → PNG
  - GameObject → GLB (via glTFast)
- Persist job runs to disk (job history snapshots + output file paths).

---

## Requirements

- Unity: 2021.3+ (recommended)
- Dependencies:
  - **Newtonsoft Json**
  - **glTFast** (for GLB export)

> Note: If you already use these in your project, you’re good. Otherwise install them via Package Manager.

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

1. **Open Project Settings**
   - `Edit → Project Settings → Atlas Workflow`
   - Set your **Asset Save Folder** (must be inside `Assets/`).

2. **Add a workflow JSON**
   - Import / select a workflow definition JSON.
   - The plugin reads it and creates the input/output parameter state.

3. **Set inputs**
   - Primitive inputs: bool / number / string
   - Asset inputs: image / mesh (from Project) or a direct File Path

4. **Run**
   - The plugin uploads required files, executes the workflow, and downloads the results.

Outputs that are files (images/meshes) are downloaded to a temporary folder and returned as local paths.

---

## Where files go

- Temporary export folder (textures / meshes / downloaded results):
  - OS temp directory under a `UnityAtlasWorkflow` folder.
- Job history:
  - Stored **next to your Unity project**, outside `Assets/`, under an `AtlasWorkflowJobs` folder.

---

## Repository structure

com.atlas.workflow/
├─ Editor/
├─ Tests/
├─ Docs~/
│ └─ Images/
│ └─ banner.png
├─ package.json
├─ README.md
└─ LICENSE

---

## Add an image / banner to this README

1. Create a folder in the repo:


---

## License

MIT — see the `LICENSE` file.

---

## Notes

- This plugin is Editor-focused and expects a reachable Atlas backend
- Workflow schemas and APIs may evolve

---