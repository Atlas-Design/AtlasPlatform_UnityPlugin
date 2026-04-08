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
  <a href="#-what-you-can-do">What you can do</a> •
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

## 📸 Screenshots

<p align="center">
  <img src="Docs~/Images/EditorWindow.png" alt="Main workflow window" width="80%"/>
  <br/>
  <em>Main window — workflows, inputs, and run</em>
</p>

<p align="center">
  <img src="Docs~/Images/JobHistory.png" alt="Job history" width="80%"/>
  <br/>
  <em>Browse and reopen past runs</em>
</p>

<p align="center">
  <img src="Docs~/Images/Settings.png" alt="Settings" width="80%"/>
  <br/>
  <em>Project settings — where saves go and how long to wait</em>
</p>

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

1. Open **Atlas → Atlas Workflow**.
2. Open **Atlas → Atlas Workflow Settings** and set **Asset Save Folder** (must be under `Assets/`) and **API Timeout** if you need longer runs.
3. Click **Import** and choose the **workflow file** (.json) your team gave you. It appears in the library dropdown.
4. Fill in the fields (images and models can come from the project or from disk via **Browse**).
5. Click **Run**. Open **Atlas → Atlas Job History** to see **Running Jobs** and **Job History** when you want a dedicated view.
6. When a run finishes, use the buttons on each output to **import** into the project or **show in Explorer** where available.

**Batch (optional):** **Atlas → Atlas Batch** → choose the same workflow → add rows (each row is one run) → **Run batch**. To stop a batch you started there, use **Cancel batch** in that window. In **Atlas Job History**, use **Batches** to group batch runs, then open a batch to see its jobs; **← Batches** goes back.

---

## ⚙️ Settings

Open **Atlas → Atlas Workflow Settings** (or **Edit → Project Settings → Project → Atlas Workflow**).

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
