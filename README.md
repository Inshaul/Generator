# Generator

## Overview

This Unity project implements a procedural 2D level generator designed around the theme of a **zombie apocalypse** taking place on an **archipelago of islands**. The player begins as a lone survivor attempting to navigate the fragmented world in search of a mysterious, glowing **abandoned research lab**, while **autonomous zombie agents** roam across islands and even **chase civilians** if spotted. The level layout, enemies, environment, and game objects are generated procedurally, with no hand-authored scenes.

The level is created in three stages as per the ECS7016P coursework guidelines:

1. **Stage 1**: Random grid initialization using a fill percentage.
2. **Stage 2**: Cellular Automata smoothing to form natural island shapes.
3. **Stage 3**: Final map processing, region detection, and placement of:
   - A research lab (goal)
   - The player (start point)
   - Zombies (autonomous enemies)
   - Civilians (chased if spotted)
   - Trees (decorative)

This system meets and exceeds the brief's expectations by including **autonomous agents** (zombies and civilians) that interact with the world, fulfilling the additional feature requirement.

---

## Key Features

### 🔹 Procedural Island Generation
- **Cellular Automata Algorithm** forms cohesive landmasses from noisy input.
- **Region segmentation** ensures each island is treated independently.
- **Water and small region filtering** guarantees gameplay-suitable islands.

### 🔹 Lab, Player, and Object Placement
- The **lab** (yellow cube with point light) spawns on a large island.
- The **player** (cylinder with light) spawns on the safest available island (fewest zombies).
- **Zombies** (red cubes) are distributed based on island size using a density factor.
- **Trees** (green spheres) decorate land with customizable density.

### 🔹 Autonomous Agents
- Zombies rotate towards the lab and wander across their island.
- A subset of zombies can **spot and chase civilians** in their view range.
- **Civilians** (cyan cubes) wander and flee when chased.

### 🔹 Visual Enhancements
- **Gizmos** color-code islands and display object placement for debugging.
- Point lights on lab and player improve visibility and guide player focus.

---

## AI & Additional Features

The project includes **custom autonomous agents** with simple yet effective behavior:

- **Zombies**
  - Face and wander toward the lab.
  - Move randomly on their assigned island.
  - If a civilian is within a set detection range, they chase them instead.
  
- **Civilians**
  - Move randomly on their island.
  - Flee away from zombies when they detect them.

These behaviors are implemented using Unity’s `Transform` and physics operations. No external AI packages were used. Instead, custom logic handles movement, line-of-sight detection, and response behaviors.

---

## Usage Instructions

### 💻 Unity Version
This project was developed and tested with **Unity 2023.1.13f1**, meeting the ECS7016P version requirement.

### ▶️ Playing the Scene
1. Open `Main.unity`.
2. Press **Play** in Unity Editor to generate a level.
3. Click **Left Mouse Button** during play to regenerate the layout.

### 🧭 Inspector Controls
Inside the **MapGenerator** script component:
- `Width` / `Height`: Size of the 2D map.
- `Random Fill Percent`: Controls initial land density.
- `Use Random Seed`: Toggle random or fixed layout.
- `Zombie Density Factor`: Adjusts zombies per island.
- `Allow Zombie Wandering`: Toggles enemy movement.
- `Zombie Move Speed`: Controls patrol speed.
- `Tree Spawn Chance`: Controls decorative tree density.

### 🧱 Prefabs Overview
| Element   | Appearance     | Description                                   |
|-----------|----------------|-----------------------------------------------|
| Lab       | Yellow Cube     | The goal; includes a light for visibility     |
| Player    | Cylinder (Light)| Survivor start; not controllable              |
| Zombie    | Red Cube        | Autonomous enemy agent                        |
| Civilian  | Cyan Cube       | Randomly wandering human agents               |
| Tree      | Green Sphere    | Decorative element on land tiles              |

All elements are generated at runtime and fully procedural.

---

## Structure & Scripts

### 🔧 Main Script: `MapGenerator.cs`

Located on an empty GameObject, this script controls all logic:
- **`GenerateMap()`** – Triggers full map generation.
- **`PopulateMap()`** – Initializes noisy land-water grid.
- **`SmoothMap()`** – Applies Cellular Automata.
- **`ProcessMap()`** – Finalizes islands, places objects.
- **`RotateZombiesTowardLab()`** – Makes enemies face the lab.
- **`MoveZombiesRandomly()`** – Handles random patrol movement.
- **`SpawnCivilians()`** – Places civilians on land.
- **`UpdateCivilians()`** – Handles civilian flee logic if chased.

Additional dictionaries and logic handle:
- Zombie-to-island mapping
- Movement targets and wandering
- Line-of-sight checks between zombies and civilians

---

## Evaluation

This generator meets the design brief by:
- Generating islands surrounded by ocean.
- Spawning hazards (zombies) and goal (lab).
- Supporting exploration and environmental storytelling.
- Including autonomous agents with emergent behavior.
- Remaining lightweight and easily extensible.

Performance is optimized for a **100x100 map** size and smooth object spawning. The resulting levels vary each playthrough, showcasing **diversity and replay value**.

---

## Credits and References

### Procedural Generation Algorithm
Based on the framework from **Sebastian Lague’s Procedural Cave Generation** tutorial. The initial noise map and smoothing algorithm (Stage 2) are adapted from this source.

### Design & Code
All Stage 1 and Stage 3 logic, including island region detection, lab/zombie/player/civilian placement, object management, and agent behavior, was designed and implemented by the student.

### Additional Feature – Autonomous Agents
The inclusion of **zombies and civilians with patrol and flee behavior** fulfills the additional feature requirement. AI behaviors are implemented using Unity’s `Transform` system and proximity detection logic.

### AI Assistance
**ChatGPT (by OpenAI)** was used for:
- Debugging assistance
- Code design refinement
- Writing detailed comments
- Readme formatting

All AI-generated suggestions were reviewed, tested, and adapted by the student to fit the unique needs of the project.

### Unity Version
Unity **2023.1.13f1** — fully compatible with coursework expectations.

### Assets
All prefabs are custom-made using **Unity primitives**:
- No third-party models, textures, or sounds were used.
- All assets are kept minimal to stay within the size limit.

### Acknowledgements
Thanks to:
- **Course instructors** for guidance.
- **Sebastian Lague** for foundational generation techniques.
- **OpenAI's ChatGPT** for support during development and documentation.

---

## Submission Notes

- All changes were implemented in the provided `Main.unity` scene.
- Core logic exists in `MapGenerator.cs` as per assignment rules.
- The project is zipped and under the 50MB requirement.
