"""Bundle out/props/*.glb + props.json into out/game/props.js (window.PROPS = {name: base64 glb}, window.PROPS_META = props.json)."""
import base64, json, os, glob
D = "/home/claude/out/props"; meta = json.load(open(f"{D}/props.json"))
props = {}
for f in sorted(glob.glob(f"{D}/*.glb")):
    n = os.path.basename(f)[:-4]; props[n] = base64.b64encode(open(f, "rb").read()).decode()
with open("/home/claude/out/game/props.js", "w") as out:
    out.write("window.PROPS=" + json.dumps(props) + ";\n"); out.write("window.PROPS_META=" + json.dumps(meta) + ";\n")
print(len(props), "props bundled", os.path.getsize("/home/claude/out/game/props.js") // 1024, "KB")
