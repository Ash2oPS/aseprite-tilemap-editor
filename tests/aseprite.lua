local Map = dofile(app.fs.joinPath(app.params.root, "map.lua"))
local assets = Map.load(app.params.root)
assert(assets.tileSize == 16)
local plan = Map.newPlan(assets, 5, 4)
local ground, details = table.unpack(Map.group(plan).layers)
local rock = assets.brushes.stone.parts[1].index
assets.brushes.stone.parts = { { x = 0, y = -1, index = rock }, { x = 1, y = -1, index = rock } }
for _, layer in ipairs { ground, details } do
    app.layer = layer
    app.useTool { tool = "pencil", layer = layer, brush = Brush(1),
        color = Color { index = layer == ground and assets.paletteIndexes.stone or assets.paletteIndexes.grass },
        points = { layer == ground and Point(1, 1) or Point(3, 2) } }
end
app.layer = ground
app.useTool { tool = "pencil", layer = ground, brush = Brush(1),
    color = Color { index = assets.paletteIndexes.grass }, points = { Point(3, 1) } }
Map.setLayerSolid(ground, true)
Map.setLayerSolid(details, false)
local output = app.params.output
Map.export(assets, plan, output)
local reopened = assert(app.open(output))
assert(reopened.width == 80 and reopened.height == 64 and #reopened.layers == 3)
assert(reopened.layers[1].data == "AsepriteTilemap:Passable")
assert(reopened.layers[2].data == "AsepriteTilemap:Passable")
assert(reopened.layers[1].isTilemap and reopened.layers[2].isTilemap)
local collision = reopened.layers[3]
assert(collision.isTilemap and not collision.isVisible and collision.data == "AsepriteTilemap:Collision:0")
local function containsTile(layer, x, y)
    local cel = layer:cel(1)
    local dx, dy = x - cel.position.x // 16, y - cel.position.y // 16
    return dx >= 0 and dy >= 0 and dx < cel.image.width and dy < cel.image.height
        and app.pixelColor.tileI(cel.image:getPixel(dx, dy)) > 0
end
assert(containsTile(reopened.layers[1], 1, 0) and containsTile(reopened.layers[1], 2, 0))
assert(not containsTile(reopened.layers[1], 1, 1))
assert(containsTile(collision, 1, 1) and not containsTile(collision, 1, 0))
assert(not containsTile(collision, 2, 0) and not containsTile(collision, 3, 2))
assert(containsTile(reopened.layers[1], 3, 1) and containsTile(collision, 3, 1))
assert(reopened.gridBounds.width == 16 and reopened.gridBounds.height == 16)
print("PASS: a two-piece stamp renders both pieces and collides only at its painted anchor.")
