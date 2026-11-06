// Translates existing project metadata to Unity's JsonUtility schema.
const fs = require('fs');
const path = require('path');
const project = path.resolve(__dirname, '..');
const godot = path.resolve(project, '..');
const target = path.join(project, 'Assets/GameAssets/Ported');
const village = path.join(target, 'ElderboomVillage');
const slots = JSON.parse(fs.readFileSync(path.join(village, 'mesh_materials.json')));
const chapel = JSON.parse(fs.readFileSync(path.join(godot, 'maps/elderboom_chapel_parts.json')));
const routes = JSON.parse(fs.readFileSync(path.join(godot, 'assets/imported/ganges_river_pebbles/routes.json')));
const data = {
  slots: Object.entries(slots).map(([key, groups]) => ({key, groups: groups.map(g => ({names: g.map(s => Array.isArray(s) ? s[0] : s)}))})),
  chapel: chapel.parts,
  routes: routes.routes.map(r => ({id:r.id, width:r.width, points:r.points.map((p,i)=>({x:p[0],z:p[1],width:r.widths?.[i] || r.width}))}))
};
fs.writeFileSync(path.join(target, 'ElderboomPortData.json'), JSON.stringify(data));
console.log(`Converted ${data.slots.length} material records, ${data.chapel.length} chapel parts, ${data.routes.length} roads.`);
const balanceSource=fs.readFileSync(path.join(godot,'weapon_balance.gd'),'utf8');
const profiles=JSON.parse(balanceSource.split('const PROFILES := ')[1].split('\n\nconst CATEGORY_DEFAULTS')[0]);
const definitions=[
 ['AKMN Zenitco','AKMN ZENITCO','assault_rifle',30,120,'Assets/GameAssets/Soph/Soph_AKMN_Plasma.glb','primary'],
 ['SIG 550','SIG 550','assault_rifle',30,120,Root('Eli/Eli_Acoustic_Ignore.glb'),'primary'],
 ['Striker-12','STRIKER-12','shotgun',12,72,Root('Tactical/Striker-12-shockwaveattack.glb'),'primary'],
 ['Chiappa Rhino 60DS','RHINO 60DS','handgun',6,36,Root('Sidearms/RhinoFPS_Idle.glb'),'rhino'],
 ['1911','1911','handgun',8,56,Root('Sidearms/1911_idle.glb'),'m1911'],
 ['Pistola di servizio','SERVICE PISTOL','handgun',12,60,Root('Glock/Glock 17/Glock17.fbx'),'service'],
 ['Revolver pesante','HEAVY REVOLVER','handgun',6,36,Root('Arsenal/pistols/animated_pistol/source/pistolAnimated.fbx'),'revolver'],
 ['AR-9','AR-9','assault_rifle',30,120,Root('Arsenal/assault/fps_ak/source/arms@ak.fbx'),'ar9'],
 ['Carabina V','V CARBINE','assault_rifle',20,80,Root('Arsenal/assault/heavy_rifle/source/arms@heavyrifle.fbx'),'carbine'],
 ['Shotgun','SHOTGUN','shotgun',8,48,Root('Arsenal/shotguns/shotgun/source/arms@shotgun.fbx'),'shotgun'],
 ['Sawnoff','SAWNOFF','shotgun',2,30,Root('Arsenal/shotguns/sawnoff/source/arms@sawnoff.fbx'),'sawnoff'],
 ['Pipe Wrench','PIPE WRENCH','melee',1,0,Root('Props/pipe_wrench_2k/pipe_wrench_2k.fbx'),'wrench'],
 ['Crowbar','CROWBAR','melee',1,0,Root('Arsenal/melee/crowbar/source/arms@crowbar.fbx'),'crowbar'],
 ['Grenade Launcher','GRENADE LAUNCHER','assault_rifle',6,30,Root('SpecialDrops/grenadelauncher/source/arms@grenadelauncher.fbx'),'grenade'],
 ['Minigun','MINIGUN','assault_rifle',120,360,Root('SpecialDrops/minigun/source/arms@minigun.fbx'),'minigun'],
 ['Flamethrower','FLAMETHROWER','assault_rifle',100,300,Root('SpecialDrops/flamethrower/source/arms@flamethrower.fbx'),'flame']
];
function Root(p){return 'Assets/GameAssets/Ported/'+p;}
const weapons=definitions.map(([key,name,category,capacity,reserve,asset,id])=>({id,name,category,capacity,reserve,asset,resource:'Elderboom/Weapons/'+id, ...profiles[key],minimumDamage:profiles[key].minimum_damage,falloffStart:profiles[key].falloff_start,fireInterval:profiles[key].fire_interval}));
fs.mkdirSync(path.join(project,'Assets/Resources/Elderboom'),{recursive:true});
fs.writeFileSync(path.join(project,'Assets/Resources/Elderboom/Weapons.json'),JSON.stringify({weapons}));
console.log(`Converted ${weapons.length} live weapon profiles.`);
