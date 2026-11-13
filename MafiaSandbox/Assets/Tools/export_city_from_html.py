E = lambda js, arg=None, t=900: asyncio.wait_for(pg.evaluate(js, arg), t)
await pg.add_script_tag(type="module", content="""
import { GLTFExporter } from 'https://cdn.jsdelivr.net/npm/three@0.160.0/examples/jsm/exporters/GLTFExporter.js';
window.__GLTFExporter = GLTFExporter;
""")
await pg.wait_for_function("() => !!window.__GLTFExporter", timeout=60000)
res = await E("""async () => {
  const d = window.__dbg, THREE = d.THREE, sc = d.scene;
  const b = d.buildings[0]; const bkeys = Object.keys(b);
  const grp = new THREE.Group(); grp.name = 'CityStatic';
  let tris = 0, nm = 0;
  for (const o of sc.children) {
    if (!o.isMesh || o.userData.prop || o.userData.vehicle) continue;
    if (o.geometry && o.geometry.attributes.position) { const g = o.geometry; tris += (g.index ? g.index.count : g.attributes.position.count) / 3; }
    const c = o.clone(); c.name = o.name || ('static_' + (nm++)); grp.add(c);
  }
  const exp = new window.__GLTFExporter();
  const buf = await new Promise((ok, err) => exp.parse(grp, ok, err, {binary: true, onlyVisible: false}));
  const u8 = new Uint8Array(buf); let s = ''; for (let i = 0; i < u8.length; i += 0x8000) s += String.fromCharCode.apply(null, u8.subarray(i, i + 0x8000));
  const props = d.placedArr.map(m => ({n: m.userData.prop, p: [+m.position.x.toFixed(3), +m.position.y.toFixed(3), +m.position.z.toFixed(3)], ry: +m.rotation.y.toFixed(4), s: +m.scale.x.toFixed(3)}));
  const vehicles = d.v.map(v => ({n: v.name, p: [+v.pos.x.toFixed(3), +v.pos.y.toFixed(3), +v.pos.z.toFixed(3)], ry: +v.heading.toFixed(4), ai: !!v.ai, player: !!v.isPlayerCar, len: v.len, wid: v.wid}));
  const buildings = d.buildings.map(b => ({n: b.name, kind: b.kind, label: b.label, p: [+b.pos.x.toFixed(3), +(b.y || 0).toFixed(3), +b.pos.z.toFixed(3)], ry: +b.rotY.toFixed(4), min: [b.min.x, b.min.z], max: [b.max.x, b.max.z], door: b.door && [b.door.x, b.door.y, b.door.z], counter: b.counter && [b.counter.x, b.counter.y, b.counter.z], owner: b.owner ? b.owner.name : null, meta: {solids: b.meta.solids, shelves: b.meta.shelves, seats: b.meta.seats, keeper: b.meta.keeper, safe: b.meta.safe, size: b.meta.size}}));
  const colliders = d.colliders.map(c => ({min: [+c.min.x.toFixed(3), +c.min.y.toFixed(3), +c.min.z.toFixed(3)], max: [+c.max.x.toFixed(3), +c.max.y.toFixed(3), +c.max.z.toFixed(3)], opaque: !!c.opaque, bld: !!c.building}));
  const vNodes = d.vNodes.map((n, i) => ({i, x: n.x, y: n.y || 0, z: n.z, adj: n.adj.map(a => d.vNodes.indexOf(a))}));
  const pNodes = d.pNodes.map((n, i) => ({i, x: n.x, y: n.y || 0, z: n.z, adj: n.adj.map(a => d.pNodes.indexOf(a))}));
  const locations = d.LOCATIONS.map(l => ({name: l[0], x: l[1], z: l[2], yaw: l[3]}));
  const home = {x: -60.5, y: 5, z: 112};
  return {glb: btoa(s), tris, nm, bkeys, vkeys: Object.keys(d.v[0]), json: JSON.stringify({props, vehicles, buildings, colliders, vNodes, pNodes, locations, home})};
}""")
import base64
open('/tmp/City_Static.glb', 'wb').write(base64.b64decode(res['glb'])); open('/tmp/city.json', 'w').write(res['json'])
print({k: res[k] for k in ('tris', 'nm', 'bkeys', 'vkeys')}, len(res['glb']) // 1024, 'KB b64', len(res['json']) // 1024, 'KB json')
