# Elderboom architecture library

Generated source and canonical GLB/FBX packages are retained outside Unity in
`assets/generated/elderboom_architecture_2026_11`. Game textures are capped at 2048 pixels.

The library includes four buildings, two masonry walls, five fence modules,
two glasshouses, two fountains, and seven individually designed credit gates.
Buildings are exterior shells; glasshouse glazing is separate geometry/material.
Each prefab uses three LODs. Fence dimensions refer to post centre spacing.

Gate choices follow photographs rendered from the actual map approaches in
`Logs/ArchitectureKit/Access_*_nearA.jpg`. Gate roots use the existing map convention:
root 1.35 metres above the walkable surface. Posts remain fixed; two hinge children
open after purchase. Area names, prices and clear widths are preserved in each spec.

The generated prefabs are a library. Existing map gate objects are not automatically
replaced by the importer. Do not stack a new prefab over an old active gate.
Opening clearance validation is recorded in `Logs/ArchitectureKit/prefab-validation.txt`.
Validate final swinging clearance against nearby scenery when placing each gate.

Generated architectural/gate surfaces: Hyper3D task receipts and source hashes in
the canonical packages. Procedural timber/glasshouse geometry: original Blender
construction, using Poly Haven Weathered Brown Planks and Stone Wall 05 (CC0).
Poly Haven source/license records: `Assets/ThirdParty/PolyHaven/Environment/LICENSES.md`.
