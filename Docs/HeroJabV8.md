# Hero02 jab v8

This delivery contains the current reviewed Blender jab and a mesh-bound FBX
export. Both are in `ArtSource/Hero02/Jab/`, outside Unity's `Assets` directory
so opening the project does not automatically convert the Blender source or
replace the existing combat controller.

## Files

- `Hero_Jab_v8.blend`: exact authoring file, including the packed character
  textures and previous animation actions. Open the `Hero_Jab_Preview` scene;
  the active action is `Hero_Jab_Left_v8_Extended`.
- `Hero_Jab_v8.fbx`: only the jab character mesh, its 33-bone rig and current
  action. Includes the `ForearmTwistBase.L` skinning correction and textures.

## Timing and motion

| Setting | Value |
| --- | --- |
| Frame rate | 60 fps |
| Blender action range | 1–28 |
| Action duration | 0.45 s |
| Punch contact and front-foot landing | Frame 9; 0.133333 s after clip start |
| Contact normalized time | 0.296296 |
| Lead-foot lift / forward step | 0.038 / 0.060 Blender model units |
| Elbow at impact | Approximately 172 degrees |
| Root travel | No accumulated displacement; foot lifts to recover |

The front foot steps and stamps at contact, followed by a short weight settle.
The right fist stays beside the chin. The v8 revision extends the punching elbow
by about 10 degrees while preserving fist height and wrist alignment. Its
startup, recovery, body movement and foot timing are the same as v7.

## Unity handoff

Copy the FBX into a dedicated character-animation folder in `Assets` when
integrating it. Start with a **Generic** rig on the exported model to review the
authored transforms directly; disable looping and animation compression. Keep
the complete skinned rig, including the corrective forearm bone. A skeleton-only
export or an older Hero02 mesh does not carry the approved skinning correction.
Use the existing `Assets/Characters/Hero02/Hero02.mat` for the game material.

The existing Hero02 gameplay controller uses Humanoid animation. Retargeting or
switching that controller is a separate integration step and must be checked for
foot contact, finger pose and forearm deformation. No controller, prefab, scene,
hitbox timing, or combat code is changed by this delivery. Use **0.133333 s from
clip start** for contact synchronization rather than assuming the destination
importer uses the same starting-frame number as Blender.

## Validation

Exported from Blender 5.2.3 and imported back into a fresh scene at 60 fps:

- Duration: 0.45 s; 33 bones; 81,414 mesh vertices.
- Maximum rest-joint position difference: 0.000000182 model units.
- Maximum animated-joint position difference over all 28 samples: 0.000000501.
- Imported impact elbow: 172.092 degrees.
- Imported impact fist-height difference: 0.000000298 model units.
- The original `.blend` was copied byte-for-byte without editing the animation.

Unity 6000.4.5f1 batch verification was attempted, but the environment could not
connect to its licensing client. This delivery therefore does not claim Unity
import or gameplay validation. The verified artifact is the Blender/FBX motion.

## Game integration

Integrated into the game on 2026-11-13 as the Hero02 `JAB` state (Humanoid clip baked from this
action). See `Docs/HeroJabIntegration.md` for how it was applied and what was verified.
