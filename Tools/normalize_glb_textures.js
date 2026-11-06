// Deterministic local import preparation: retain geometry, rigs and animation
// bytes exactly; downsample embedded textures in the Unity copy only.
const fs=require('fs'),os=require('os'),path=require('path'),cp=require('child_process'),crypto=require('crypto');
const [source,destination,maxArg]=process.argv.slice(2),maximum=Number(maxArg||2048);
if(!source||!destination||path.resolve(source)===path.resolve(destination))throw Error('Use separate original and Unity destination paths');
const original=fs.readFileSync(source);
if(original.readUInt32LE(0)!==0x46546c67)throw Error('Not a GLB');
const jsonLength=original.readUInt32LE(12),json=JSON.parse(original.toString('utf8',20,20+jsonLength));
const binStart=20+jsonLength+8;
const temp=fs.mkdtempSync(path.join(os.tmpdir(),'elderboom-textures-'));
const images=new Map((json.images||[]).filter(i=>i.bufferView!==undefined).map(i=>[i.bufferView,i]));
let offset=0,resized=0;const parts=[];
for(let i=0;i<json.bufferViews.length;i++)
{
 const view=json.bufferViews[i];let bytes=original.subarray(binStart+(view.byteOffset||0),binStart+(view.byteOffset||0)+view.byteLength);
 const image=images.get(i);
 if(image && bytes.readUInt32BE(0)===0x89504e47 && Math.max(bytes.readUInt32BE(16),bytes.readUInt32BE(20))>maximum)
 {
  const imagePath=path.join(temp,i+'.png');fs.writeFileSync(imagePath,bytes);
  cp.execFileSync('/usr/bin/sips',['-Z',String(maximum),imagePath],{stdio:'ignore'});bytes=fs.readFileSync(imagePath);resized++;
 }
 view.byteOffset=offset;view.byteLength=bytes.length;parts.push(bytes);offset+=bytes.length;
 const padding=(4-offset%4)%4;if(padding){parts.push(Buffer.alloc(padding));offset+=padding;}
}
json.buffers[0].byteLength=offset;
let jsonBytes=Buffer.from(JSON.stringify(json));const pad=(4-jsonBytes.length%4)%4;if(pad)jsonBytes=Buffer.concat([jsonBytes,Buffer.alloc(pad,0x20)]);
const bin=Buffer.concat(parts),header=Buffer.alloc(20),binHeader=Buffer.alloc(8);
header.writeUInt32LE(0x46546c67,0);header.writeUInt32LE(2,4);header.writeUInt32LE(20+jsonBytes.length+8+bin.length,8);header.writeUInt32LE(jsonBytes.length,12);header.writeUInt32LE(0x4e4f534a,16);
binHeader.writeUInt32LE(bin.length,0);binHeader.writeUInt32LE(0x004e4942,4);
fs.writeFileSync(destination,Buffer.concat([header,jsonBytes,binHeader,bin]));
const receipt={source,destination,maximum,resized,sourceBytes:original.length,resultBytes:fs.statSync(destination).size,sourceSha256:crypto.createHash('sha256').update(original).digest('hex'),resultSha256:crypto.createHash('sha256').update(fs.readFileSync(destination)).digest('hex'),nodes:json.nodes.length,animations:json.animations?.map(a=>a.name)||[]};
fs.writeFileSync(destination+'.normalization.json',JSON.stringify(receipt,null,2));
for(const file of fs.readdirSync(temp))fs.unlinkSync(path.join(temp,file));fs.rmdirSync(temp);
console.log(JSON.stringify(receipt));
