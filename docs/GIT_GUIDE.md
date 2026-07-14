# Git Workflow & Repository Guidelines 🛠️

This document outlines the standard Git practices, branch naming conventions, and commit formatting for the TouchpadVisualizer codebase. Following these practices ensures clean, traceable history and seamless collaboration.

---

## 🌿 Branching Strategy

We use a feature-branch workflow. All changes should target a specific branch rather than being pushed directly to the default branch.

*   **`main`**: The primary, stable branch. Code here must always compile, run, and pass verification checks.
*   **`feature/`**: New visual styles, inputs, or menus.
    *   *Example*: `feature/particle-physics`, `feature/custom-midi-tracks`
*   **`bugfix/`**: Fixes for rendering leaks, crash events, or calibration issues.
    *   *Example*: `bugfix/rawinput-flush-leak`, `bugfix/fullscreen-timer-crash`
*   **`refactor/`**: Code organization without changing functionality.
    *   *Example*: `refactor/d3d-device-setup`, `refactor/mvvm-viewmodel-cleanup`

### Branch Lifecycle
1.  Check out a fresh branch from `main`: `git checkout -b feature/your-feature-name`
2.  Develop changes, ensuring local compile checks succeed.
3.  Commit with semantic commit messages (see below).
4.  Open a Pull Request targeting `main`.

---

## 📝 Commit Message Guidelines

We enforce **Conventional Commits** formatting to keep the Git log structured and readable.

### Format
```
<type>(<scope>): <short description>

[optional body describing the 'why' of the change]
```

### Types
*   `feat`: A new feature (e.g., adding a bloom toggle in settings).
*   `fix`: A bug fix (e.g., resolving coordinate scaling for high-DPI monitors).
*   `refactor`: Code changes that neither fix a bug nor add a feature.
*   `perf`: A code change that improves performance.
*   `docs`: Documentation only changes.
*   `style`: Changes that do not affect the meaning of the code (white-space, formatting, etc.).
*   `chore`: Updating build tasks, package configurations, or ignore rules.

### Scope Examples
*   `input` (touchpad, HID interop, raw input)
*   `rendering` (Direct3D, shaders, Particle system)
*   `game` (Piano Tiles logic, MidiPlayer, View updates)
*   `settings` (AppSettings, viewmodel binds, config loading)

### Examples
*   `feat(rendering): add dual-pass gaussian blur shader for enhanced bloom`
*   `fix(input): handle tipSwitch missing from serial mode packages`
*   `docs: create comprehensive readme and git instructions`
*   `perf(rendering): optimize particle buffer instance updates`

---

## ⚠️ Repository Hygiene

*   **Never Commit Build Artifacts**: The `.gitignore` is set up to block `bin/`, `obj/`, and `.vs/` directories. If you add new target directories or projects, ensure they follow these rules.
*   **Never Commit Log Files**: Ensure logs like `debug.log` are kept local. If Git tracks a log file, remove it using:
    ```bash
    git rm --cached debug.log
    ```
*   **Keep Assets Text-Based**: HLSL shaders should remain as `.hlsl` source files copied to output directories, rather than pre-compiled binary payloads, to allow source tracking and code review inside Git.

---

## 🔍 Verification Before Merging

Prior to opening a pull request, perform the following validation pipeline:

1.  **Clean and Restore**:
    ```bash
    dotnet clean
    dotnet restore
    ```
2.  **Compile Check**:
    ```bash
    dotnet build -c Release
    ```
3.  **Local Test Run**:
    Launch the compiled visualizer and ensure inputs trigger visualizers, keys respond correctly, and settings persist safely.
