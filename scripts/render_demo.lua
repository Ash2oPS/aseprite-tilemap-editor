local Map = dofile(app.fs.joinPath(app.params.root, "map.lua"))
local assets = Map.load(app.params.root)
local layout = {
    "..............",
    "..ggggg.......",
    ".gsggggggggg..",
    ".ggg..gggsgg..",
    ".ggg..ggggggg.",
    ".gggggggg..gg.",
    "..gggsggggggg.",
    "...ggggggggg..",
    "..............",
}
local plan = Map.newPlan(assets, #layout[1], #layout)
local ground, details = table.unpack(Map.group(plan).layers)
local groundImage = Image(plan.spec)
local detailImage = Image(plan.spec)

for y, row in ipairs(layout) do
    for x = 1, #row do
        local cell = row:sub(x, x)
        if cell ~= "." then
            groundImage:drawPixel(x - 1, y - 1, assets.paletteIndexes.grass)
        end
        if cell == "s" then
            detailImage:drawPixel(x - 1, y - 1, assets.paletteIndexes.stone)
        end
    end
end
plan:newCel(ground, 1, groundImage)
plan:newCel(details, 1, detailImage)

Map.export(assets, plan, app.params.tilemap)
local tilemap = assert(app.open(app.params.tilemap))
local planImage = Image(plan.width, plan.height, ColorMode.RGB)
planImage:drawSprite(plan, 1)
planImage:resize(tilemap.width, tilemap.height)

local tilemapImage = Image(tilemap.width, tilemap.height, ColorMode.RGB)
tilemapImage:drawSprite(tilemap, 1)

local border, gap = 12, 16
local demo = Image(tilemap.width * 2 + gap + border * 2, tilemap.height + border * 2, ColorMode.RGB)
demo:clear(Color { r = 32, g = 36, b = 42 })
demo:drawImage(planImage, Point(border, border))
demo:drawImage(tilemapImage, Point(border + tilemap.width + gap, border))
assert(demo:saveAs(app.params.output), "Could not save the demo image.")
