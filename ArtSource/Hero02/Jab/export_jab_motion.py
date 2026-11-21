# Hero02 잽 v8을 Unity 굽기 도구(HeroJabBaker)가 읽는 동작 파일로 내보낸다.
#   blender -b ArtSource/Hero02/Jab/Hero_Jab_v8.blend --python ArtSource/Hero02/Jab/export_jab_motion.py
# 결과: Assets/Characters/Hero02/ApprovedJab/Hero_JAB.motion.json (방어 동작의 Hero_Guard_*.motion.json과 같은 형식)
# 뼈마다 "기준 자세(승인 기본자세 2번째 프레임)에서 얼마나 돌았는지"를 뼈대 공간에서 적는다. 그래서 두 프로그램의
# 뼈 축이 달라도 되고, Unity는 자기 기본자세 위에 같은 회전을 얹는다. 교정용 뼈 ForearmTwistBase.L은 게임 모델에 없어 뺀다.
import bpy, json, os

repo = os.path.abspath(os.path.join(os.path.dirname(bpy.data.filepath), "..", "..", ".."))
hero = os.path.join(repo, "Assets", "Characters", "Hero02")
ref = json.load(open(os.path.join(hero, "ApprovedGuard", "Hero_Guard_High.motion.json"), encoding="utf-8"))
names, parents = ref["bones"], ref["parents"]   # 게임 모델의 뼈 32개, 부모가 먼저 오는 순서

def pose(scene, rig, frame):
    scene.frame_set(frame)
    graph = scene.view_layers[0].depsgraph; graph.update()
    bones = rig.evaluated_get(graph).pose.bones
    return {n: bones[n].matrix.copy() for n in names}

idle_scene, jab_scene = bpy.data.scenes["Hero_Idle_Jin_v4"], bpy.data.scenes["Hero_Jab_Preview"]
idle_rig, jab_rig = bpy.data.objects["Hero_Jin_v4_Rig"], bpy.data.objects["Hero_Jab_Rig"]
assert jab_rig.animation_data.action.name == "Hero_Jab_Left_v8_Extended", jab_rig.animation_data.action.name
assert jab_scene.render.fps == 60 and (jab_scene.frame_start, jab_scene.frame_end) == (1, 28)
for n in names:   # 두 뼈대의 기본 뼈 배치가 같아야 기준 자세를 같이 쓸 수 있다
    a, b = idle_rig.data.bones[n].matrix_local, jab_rig.data.bones[n].matrix_local
    assert max(abs(a[i][j] - b[i][j]) for i in range(4) for j in range(4)) < 1e-6, n

base = pose(idle_scene, idle_rig, 2)
q0 = {n: base[n].to_quaternion() for n in names}
out = {"fps": 60, "bones": names, "parents": parents, "basePos": [round(c, 6) for n in names for c in base[n].translation], "clips": []}
hips, rot, pos = [], [], []
frames = range(jab_scene.frame_start, jab_scene.frame_end + 1)
for f in frames:
    M = pose(jab_scene, jab_rig, f)
    hips += [round(c, 6) for c in M["Hips"].translation]
    for n in names:
        d = M[n].to_quaternion() @ q0[n].inverted(); d.normalize()
        if d.w < 0: d = -d
        rot += [round(d.w, 6), round(d.x, 6), round(d.y, 6), round(d.z, 6)]
        pos += [round(c, 5) for c in M[n].translation]   # 관절 위치. 옮겨진 결과를 확인하는 데만 쓴다
out["clips"].append({"name": "Hero_JAB", "loop": False, "frames": len(frames), "hips": hips, "rot": rot, "pos": pos})
target = os.path.join(hero, "ApprovedJab", "Hero_JAB.motion.json")
os.makedirs(os.path.dirname(target), exist_ok=True)
open(target, "w", encoding="utf-8").write(json.dumps(out, separators=(",", ":")))
print("wrote", target, len(frames), "frames")
