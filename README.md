<p align="center">
  <img src="Docs~/Images/Banner.png" alt="Atlas Workflow Banner" width="100%"/>
</p>

<h1 align="center">Atlas Workflow</h1>

<p align="center">
  <strong>Run Atlas Platform workflows inside Unity — no coding required</strong>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/Unity-2023.1+-black?logo=unity" alt="Unity 2023.1+"/>
  <img src="https://img.shields.io/badge/License-MIT-blue.svg" alt="MIT License"/>
  <img src="https://img.shields.io/badge/Version-0.1.0-green" alt="Version 0.1.0"/>
  <img src="https://img.shields.io/badge/Status-Early%20Development-orange" alt="Status"/>
</p>

<p align="center">
  <a href="#-atlas-workflow-single-run">Single run</a> •
  <a href="#-atlas-batch">Batch</a> •
  <a href="#-atlas-job-history">Job history</a> •
  <a href="#-installation">Installation</a> •
  <a href="#-quick-start">Quick Start</a> •
  <a href="#-settings">Settings</a> •
  <a href="#-troubleshooting">Troubleshooting</a>
</p>

---

## 🎯 What this is

**Atlas Workflow** connects Unity to **Atlas Platform**: you load a workflow your team or Atlas provides, fill in the fields, press run, and get results (images, 3D models, audio files, and simple values) back in the editor.

You stay inside familiar Unity menus and windows — the plugin handles talking to the platform and saving your runs so you can look them up later.

---

## ✨ What you can do

- **Run a workflow once** — **Atlas → Atlas Workflow**: pick a workflow, set inputs, run, see outputs.
- **Run many variations** — **Atlas → Atlas Batch**: same workflow, multiple rows of inputs; save drafts, run or cancel the whole queue.
- **Watch progress and history** — **Atlas → Atlas Job History**: see what’s running, open past runs, and switch between a **Jobs** list and a **Batches** view when you use batch runs.
- **Bring results into the project** — import or reveal generated files from the UI when offered (textures, models, audio, etc.).
- **Adjust basics** — **Atlas → Atlas Workflow Settings** for save folder, how long to wait, and notifications.

> **Note:** You need an **internet connection** and **access to Atlas Platform** (as set up in your workflow file). Audio **outputs** are supported; uploading audio **as an input** is not supported in this version.

---

## 🪟 Atlas Workflow (single run)

**Menu:** **Atlas → Atlas Workflow**

This window is for **one workflow at a time**: pick what to run, fill in inputs, see outputs, and start a single run.

### Main areas

| Area | What it’s for |
|------|----------------|
| **Workflow Library** | **Dropdown** — switch between workflows you’ve already imported. **Import** — add a new workflow from a `.json` file your team shared. **✕** — remove the selected workflow from the library (does not delete the file on disk). |
| **Selected Workflow** | Shows the **name and details** of the current workflow, then **Inputs** (everything you must set before running), then **Outputs** (what you’ll get back — previews and paths fill in after a successful run). |
| Bottom row | **Open Job History** — jumps to **Atlas → Atlas Job History** to watch runs or browse the past. **▶ Run …** — starts one execution using the values you entered (the button label includes the workflow name). |

### Typical flow

1. **Import** a workflow once, or pick an existing one in the dropdown.  
2. Set each input (textures/models from the project or **Browse** to a file on disk; toggles, numbers, and text as shown).  
3. Press **▶ Run …** and wait for the platform to finish.  
4. Use the output rows to **import** assets into the project or **reveal** files on disk when those actions appear.

<p align="center">
  <img src="Docs~/Images/EditorWindow.png" alt="Atlas Workflow window with Workflow Library and Selected Workflow" width="85%"/>
  <br/>
  <em>Atlas Workflow — library at the top, selected workflow with inputs/outputs below</em>
</p>

> **Image polish (optional):** Replace or supplement with `Docs~/Images/EditorWindow_Annotated.png` — same window with subtle callouts or numbered labels for **Workflow Library**, **Selected Workflow**, **Open Job History**, and **Run** (sample content only, no secrets).

---

## 📋 Atlas Batch

**Menu:** **Atlas → Atlas Batch**  
**Window title:** **Atlas Batch**

Use this when you need the **same workflow many times** with **different inputs per row** (for example many variants or a small overnight queue). Each row becomes its **own saved run** in job history, grouped so you can review them together later.

### Main areas

| Area | What it’s for |
|------|----------------|
| **Workflow Library** | Same idea as the single-run window: **Import**, **dropdown**, and **✕** to manage which workflows exist in your library. |
| **Selected Workflow** | After you choose a workflow, this shows its **name** and a **read-only summary** of what inputs the batch will use. Below that, **one block per row** — each row is one full set of inputs for that workflow. Use **+ Add row**, **Duplicate**, **Remove**, **↑** / **↓** to build your queue. **Save batch…** / **Load batch…** store or restore a draft (your workflow must still be in the library when you load). |
| **Run settings** | Controls how the batch runs, such as **how many jobs at once** and **how many times to retry** when a run fails for a temporary reason (for example a network blip). Exact labels match the fields in the window. |
| Bottom bar | **Open Job History** — same as in the single-run window. **Run batch** — start every row. **Cancel batch** — stop adding new runs and cancel in-flight work from *this* batch (this is different from **Dismiss** on a card in the Job History window). |

### Typical flow

1. Import or select a workflow.  
2. Add rows and fill each row’s inputs.  
3. Adjust **Run settings** if your team recommends it.  
4. **Run batch** — follow progress in **Atlas → Atlas Job History**.  
5. **Save batch…** if you want to reuse the same queue later.

> **Screenshot to add / clarify:** `Docs~/Images/WorkflowBatchEditor.png` — Full **Atlas Batch** window: **Workflow Library**, **Selected Workflow** with **at least two rows** visible, **Run settings**, and footer with **Open Job History**, **Cancel batch**, **Run batch**. Use a friendly sample workflow and non-sensitive assets. Prefer good lighting, consistent editor theme, and enough window height to show the scroll area.

> **Extra (optional):** `Docs~/Images/WorkflowBatchEditor_RunSettings.png` — Tighter crop on **Run settings** only, if you document those fields in more detail later.

---

## 📚 Atlas Job History

**Menu:** **Atlas → Atlas Job History**  
**Window title:** **Atlas Job History**

This is the **mission control** view: everything **currently running** plus the **archive of past runs**. You can leave it open while you work in the single-run or batch windows.

### Main areas

| Area | What it’s for |
|------|----------------|
| **Running Jobs** | A scrollable list of **active** (and very recent) runs. Each **card** shows status and shortcuts — for example **Cancel** when this window is tied to a single run you started from **Atlas Workflow**, or **Dismiss** to hide a card you don’t need to watch (often used for batch jobs). Hover tooltips may mention **batch** position when a run came from **Atlas Batch**. |
| **Jobs History** | The lower (or main) area lists **finished and past runs**. Pick a row to see **inputs and outputs** for that run on the side (read-only snapshot of what was sent and returned). Use the **⋮** menu on the header if your build exposes extra actions (for example refresh or maintenance). |
| **Jobs** / **Batches** | At the top of the history toolbar: **Jobs** — one entry **per run** (every single and batch job). **Batches** — one entry **per batch**, then open a batch to see **only** the runs that belonged to it; **← Batches** returns to the batch list. |
| **Status**, **Type**, **Date** | Filters for the list. Hover the filters for short explanations — some options behave differently in **Jobs** vs **Batches** mode (tooltips in the editor describe this). |

### When to use which mode

- **Jobs** — “Show me everything” or find one specific run quickly.  
- **Batches** — “I ran a batch yesterday — how did the whole group do?”

<p align="center">
  <img src="Docs~/Images/JobHistory.png" alt="Jobs History list with a run selected and detail beside it" width="85%"/>
  <br/>
  <em>Jobs History — list and detail (existing asset; good for <strong>Jobs</strong> mode until a full-window capture replaces it)</em>
</p>

> **Screenshot to add / clarify:** `Docs~/Images/AtlasJobHistoryWindow.png` — Full window showing **Running Jobs** (even one sample card) and **Jobs History** with the **Jobs \| Batches** toggle and **Status / Type / Date** filters visible. Same theme as other screenshots; no API keys or private paths in frame.

> **Screenshot to add / clarify:** `Docs~/Images/JobHistoryBatchesMode.png` — **Batches** selected: either the **batch catalog** list or **drill-in** with **← Batches** visible so readers see navigation. Include at least one batch row with clear status/counts if your data allows (or anonymized samples).

---

## 📷 Documentation images checklist

For whoever prepares screenshots: use **one consistent Unity theme**, **non-sensitive** sample workflows, and save PNGs under `Docs~/Images/`. When a file exists, add a normal `<img>` in the matching section above and **remove** the corresponding “Screenshot to add” blockquote.

| File | Purpose |
|------|---------|
| `WorkflowBatchEditor.png` | Full **Atlas Batch** window (see [Atlas Batch](#-atlas-batch)). |
| `AtlasJobHistoryWindow.png` | **Running Jobs** + **Jobs History** + **Jobs \| Batches** + filters ([Atlas Job History](#-atlas-job-history)). |
| `JobHistoryBatchesMode.png` | **Batches** mode: catalog or drill-in with **← Batches**. |
| `EditorWindow_Annotated.png` | Optional callouts on single-run window. |
| `WorkflowBatchEditor_RunSettings.png` | Optional crop of **Run settings**. |
| `Settings_KeyFields.png` | Optional crop of **Asset Save Folder** + **API Timeout**. |

---

## 📦 Requirements

- **Unity** 2023.1 or newer  
- **Atlas Platform** access (your workflows are configured to use it)  
- Extra packages the plugin needs (JSON, GLB tools) are installed automatically with the package.

---

## 🚀 Installation

### From Git URL (common)

1. **Window → Package Manager** → **+** → **Add package from git URL...**
2. Paste your team’s plugin URL (for example the Atlas Platform Unity plugin repo) and **Add**.

### From disk

1. **Window → Package Manager** → **+** → **Add package from disk...**
2. Choose the folder that contains `package.json`.

### After install

Under the top menu **Atlas** you should see **Atlas Workflow**, **Atlas Batch**, **Atlas Job History**, and **Atlas Workflow Settings**.  
On newer Unity versions, an **Atlas** entry may also appear on the main toolbar (you can show or hide it from the toolbar’s right-click menu).

---

## 🏃 Quick Start

The three windows above (**Atlas Workflow**, **Atlas Batch**, **Atlas Job History**) are where you spend your time; these steps are the shortest path to a first successful run.

1. Open **Atlas → Atlas Workflow**.
2. Open **Atlas → Atlas Workflow Settings** and set **Asset Save Folder** (must be under `Assets/`) and **API Timeout** if you need longer runs.
3. Click **Import** and choose the **workflow file** (.json) your team gave you. It appears in the library dropdown.
4. Fill in the fields (images and models can come from the project or from disk via **Browse**).
5. Click **Run**. Open **Atlas → Atlas Job History** to see **Running Jobs** and **Job History** when you want a dedicated view.
6. When a run finishes, use the buttons on each output to **import** into the project or **show in Explorer** where available.

**Batch (optional):** **Atlas → Atlas Batch** → choose the same workflow → add rows (each row is one run) → **Run batch**. To stop a batch you started there, use **Cancel batch** in that window. In **Atlas Job History**, use **Batches** to group batch runs, then open a batch to see its jobs; **← Batches** goes back.

---

## ⚙️ Settings

Open **Atlas → Atlas Workflow Settings** (or **Edit → Project Settings → Project → Atlas Workflow**). These options apply to **all** workflow and batch runs, not just one window.

<p align="center">
  <img src="Docs~/Images/Settings.png" alt="Atlas Workflow project settings" width="85%"/>
  <br/>
  <em>Atlas Workflow Settings — save folder, timeouts, notifications</em>
</p>

> **Image polish (optional):** `Docs~/Images/Settings_KeyFields.png` — Crop highlighting **Asset Save Folder** and **API Timeout** for newcomers.

| What | Why it matters |
|------|----------------|
| **Asset Save Folder** | Where imported results go (inside `Assets/`). |
| **API Timeout** | How long to wait before treating a run as stuck. |
| **Notifications** | Pop-up when a run finishes. |
| **Temp storage** | Warning if temporary files grow large; you can clear or raise the limit. |
| **Verbose logging** | Only if support asks — extra detail in the **Console**. |

---

## 💡 How it works (short)

- A **workflow** is a small file that describes what to run on Atlas Platform. You don’t edit JSON in normal use — you **Import** it once and pick it from the list.
- Each time you run, the plugin creates a **job**: a saved record with your inputs and outputs so you can compare runs later.
- **Batch** runs create one job per row; **Batches** in history is just a clearer way to browse those grouped runs.

---

## 🔧 Troubleshooting

| Problem | What to try |
|--------|-------------|
| Run fails or times out | Check internet; in Settings, increase **API Timeout**; ask your team if the workflow or platform access changed. |
| Texture or model export error | For textures: in Unity’s **Inspector** for that image, enable **Read/Write** if the error says it’s not readable. |
| Audio file looks wrong or won’t import | Use **Reveal** to find the file on disk; if needed import it manually. Your team can confirm the workflow is set up for audio output. |
| Batch won’t stop | Use **Cancel batch** in **Atlas Batch** — that’s different from **Dismiss** on a card in the jobs window. |
| History looks empty or broken | Make sure you’re in **Jobs** vs **Batches** as expected; if a run failed, it may still appear with an error state. For repeated issues, enable **Verbose logging** and share the **Console** with your team. |

---

## 📄 License

This project is licensed under the **MIT License** — see the [LICENSE](LICENSE) file for details.

---

<p align="center">
  <strong>Built with ❤️ by the Atlas Team</strong>
</p>
