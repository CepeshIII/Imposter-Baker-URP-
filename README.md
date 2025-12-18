# Imposter Baker (URP)

This repository contains an **imposter baker tool for Unity URP**, based on the original **URPIMP** project by **hickVieira**.  
The tool is intended for **offline editor baking** of imposter atlases using URP’s **Deferred GBuffer** path.

The main focus of this version is **code organization, extensibility, and practical fixes** discovered during use.

---

## Based on / Credits

This project is derived from:

- **URPIMP** by *hickVieira*  
  https://github.com/hickVieira/URPIMP
- License: **CC0 (Public Domain)**

All core ideas (octahedral sampling, GBuffer usage, dilation approach) come from the original project.  
This repository builds on that base and reorganizes parts of the implementation.

---

## Tested Environment

- **Unity 6000.0.46f1**
- Universal Render Pipeline
- Editor-only tool

---

## What Changed Compared to URPIMP

### Code Structure

- The original single-script approach was split into smaller modules
- This was done mainly to improve readability and make further changes easier.

---

### Rendering Approach (Command Buffers)
- Rendering is now driven explicitly via Graphics.ExecuteCommandBuffer
- The baker builds and executes its own command buffers for each bake step

**Reason:**

When relying only on `GL` and `Graphics` calls (as in the original implementation), a reproducible issue was observed in **Unity 6000.0.46f1**:

- The first bake call produced correct results
- Subsequent bake calls (without restarting the editor) produced incorrect or corrupted output
- Re-adding the baker component or restarting the editor restored correct behavior

Switching to explicit command buffer execution ensures that:
- Render state is fully controlled by the baker
- View/projection matrices are applied deterministically
- Each bake is isolated from editor and scene view render state


This change was introduced as a **stability fix**, not a conceptual redesign.

---

### Snapshot Generation

- Snapshot logic moved into a separate `SnapshotBuilder`
- Optional rotation of sampling directions was added to bias captures for certain asset types (e.g. vertical objects)

---

### Dilation Changes

- In addition to the original full dilation pass, the baker supports:
  - partial dilation passes
  - difference-mask–based iteration
- This spreads dilation cost across multiple passes instead of a single expensive pass

---

## What Remains the Same

- Octahedral imposter layout
- Use of URP Deferred GBuffer
- Offline editor baking workflow
- Output:
  - Albedo atlas
  - Normal + depth atlas
  - Imposter mesh and material

---

## Limitations

- URP Deferred only
- No skinned mesh support
- GPU cost can be high for large atlas resolutions
- Depends on URP GBuffer layout, which may change between URP versions

---

## License

This project follows the original **CC0 (Public Domain)** license.

