local Map = {}
local pixel = app.pixelColor
local profile, profileRoot

local function readFile(filename)
    local file = assert(io.open(filename, "rb"))
    local data = file:read("*a")
    file:close()
    return data
end

function Map.configure(root)
    if profileRoot ~= root then
        profile = json.decode(readFile(app.fs.joinPath(root, "profile.json")))
        profile.propertyKey = profile.publisher .. "/" .. profile.name
        profileRoot = root
    end
    return profile
end

function Map.load(root)
    local settings = Map.configure(root)
    local path = app.fs.joinPath
    local catalog = json.decode(readFile(path(root, "resources", "tiles.json")))
    local tilesize = math.tointeger(catalog.tileSize)
    assert(catalog.schemaVersion == 2 and tilesize and tilesize > 0 and tilesize <= 512,
        "Expected schema 2 with a square tile size from 1 to 512 pixels.")
    assert(#catalog.brushOrder > 0 and #catalog.brushOrder <= 255, "Use between 1 and 255 brushes per pack.")
    local atlas = assert(Image { fromFile = path(root, "resources", "tiles.png") },
        "Could not load the terrain atlas.")
    local columns = math.tointeger(catalog.columns)
    assert(atlas.colorMode == ColorMode.RGB and columns
        and columns > 0 and atlas.width == columns * tilesize and atlas.height % tilesize == 0,
        "Expected an RGBA atlas matching the catalog grid.")

    local rects, indexes, colors, brushes = {}, {}, {}, {}
    local function atlasIndex(value)
        local index = math.tointeger(value)
        assert(index and index >= 0 and index < (atlas.height // tilesize) * columns,
            "Invalid index in the tile atlas.")
        if not rects[index] then
            rects[index] = Rectangle(index % columns * tilesize, index // columns * tilesize, tilesize, tilesize)
        end
        return index
    end
    for index, id in ipairs(catalog.brushOrder) do
        local entry = assert(catalog.brushes[id], "Missing brush: " .. id)
        assert(type(entry.color) == "string" and entry.color:match("^#%x%x%x%x%x%x$"),
            "Invalid color for " .. id)
        local rgb = tonumber(entry.color:sub(2), 16)
        assert(not colors[rgb], "Duplicate terrain color: " .. entry.color)
        colors[rgb] = id
        indexes[id] = index
        local labels = (settings.brushOverrides or {})[id] or {}
        local brush = {
            label = entry.label or id,
            category = labels.category or entry.category or (entry.sprites and "Terrain" or "Scenery"),
            anchor = labels.anchor or entry.anchor or "painted cell",
            thumbnailMask = labels.thumbnailMask or entry.thumbnailMask or 255,
            swatch = Color { r = rgb >> 16, g = (rgb >> 8) & 255, b = rgb & 255 },
        }
        if entry.sprites then
            brush.sprites = {}
            for mask, spriteIndex in pairs(entry.sprites) do
                brush.sprites[tonumber(mask)] = atlasIndex(spriteIndex)
            end
        else
            brush.parts = {}
            for _, part in ipairs(entry.parts) do
                brush.parts[#brush.parts + 1] = {
                    x = math.tointeger(part.x), y = math.tointeger(part.y), index = atlasIndex(part.index),
                }
            end
        end
        brushes[id] = brush
    end
    local orderedIndexes = {}
    for index in pairs(rects) do
        orderedIndexes[#orderedIndexes + 1] = index
    end
    table.sort(orderedIndexes)
    return {
        atlas = atlas, rects = rects, atlasIndexes = orderedIndexes, tileSize = tilesize,
        brushes = brushes, brushOrder = catalog.brushOrder, colors = colors, paletteIndexes = indexes,
        neighborhood = dofile(path(root, "terrain.lua")).neighborhood,
    }
end

function Map.addSwatches(assets, sprite)
    local original = sprite.palettes[1]
    local palette = Palette(original)
    local seen = {}
    for index = 0, #palette - 1 do
        if sprite.colorMode ~= ColorMode.INDEXED or index ~= sprite.transparentColor then
            local color = palette:getColor(index)
            if color.alpha == 255 then seen[color.rgbaPixel] = true end
        end
    end
    for _, id in ipairs(assets.brushOrder) do
        local swatch = assets.brushes[id].swatch
        if not seen[swatch.rgbaPixel] then
            assert(#palette < 256, "The plan palette has no room for all pack swatches.")
            local index = #palette
            palette:resize(index + 1)
            palette:setColor(index, swatch)
        end
    end
    app.transaction("Add Pack Swatches", function() sprite:setPalette(palette) end)
end

function Map.newPlan(assets, width, height)
    local maxCellsPerSide = math.min(512, 65535 // assets.tileSize)
    assert(math.type(width) == "integer" and math.type(height) == "integer"
        and width >= 1 and height >= 1 and width <= maxCellsPerSide and height <= maxCellsPerSide,
        ("Map dimensions must be whole numbers from 1 to %d cells."):format(maxCellsPerSide))
    local sprite = Sprite(width, height, ColorMode.INDEXED)
    local ground = sprite.layers[1]
    local palette = Palette(#assets.brushOrder + 1)
    palette:setColor(0, Color { r = 0, g = 0, b = 0, a = 0 })
    for index, id in ipairs(assets.brushOrder) do
        palette:setColor(index, assets.brushes[id].swatch)
    end
    app.transaction("Create terrain plan", function()
        sprite.transparentColor = 0
        sprite:setPalette(palette)
        local group = sprite:newGroup()
        group.name = profile.planGroup
        group.isExpanded = true
        ground.name = profile.layers[1]
        ground.parent = group
        for index = 2, #profile.layers do
            local layer = sprite:newLayer()
            layer.name = profile.layers[index]
            layer.parent = group
        end
    end)
    app.layer = ground
    if app.isUIAvailable then
        app.fgColor = Color { index = assets.paletteIndexes[profile.defaultBrush] or 1 }
    end
    return sprite
end

function Map.group(sprite)
    if sprite then
        for _, layer in ipairs(sprite.layers) do
            if layer.isGroup and layer.name == profile.planGroup then return layer end
        end
    end
end

function Map.layerSolid(layer)
    return layer.properties(profile.propertyKey).solid
end

function Map.setLayerSolid(layer, solid)
    app.transaction("Set Unity Layer Collision", function()
        layer.properties(profile.propertyKey).solid = solid
    end)
end

local function colorId(assets, red, green, blue, alpha)
    if alpha ~= 255 then return nil end
    return assets.colors[(red << 16) | (green << 8) | blue]
end

function Map.brushId(assets, color)
    return colorId(assets, color.red, color.green, color.blue, color.alpha)
end

function Map.brushColor(assets, sprite, id)
    local swatch = assert(assets.brushes[id], "Unknown brush: " .. id).swatch
    if sprite.colorMode == ColorMode.RGB then return swatch end
    assert(sprite.colorMode == ColorMode.INDEXED, "Use Indexed or RGB mode for terrain plans.")

    local palette = sprite.palettes[1]
    for index = 0, #palette - 1 do
        if index ~= sprite.transparentColor and Map.brushId(assets, palette:getColor(index)) == id then
            return Color { index = index }
        end
    end
    local index = #palette
    assert(index < 256, "The plan palette has no room for this brush.")
    palette = Palette(palette)
    palette:resize(index + 1)
    palette:setColor(index, swatch)
    app.transaction("Add Brush Swatch", function() sprite:setPalette(palette) end)
    return Color { index = index }
end

local function decodePlanLayer(assets, sprite, layer)
    local cells, occupied = {}, {}
    local cel = layer:cel(1)
    if cel then
        local image, origin = cel.image, cel.position
        local palette = sprite.palettes[1]
        local transparent = sprite.transparentColor
        local paletteIds = {}
        if image.colorMode == ColorMode.INDEXED then
            for index = 0, #palette - 1 do
                if index ~= transparent then
                    local swatch = palette:getColor(index)
                    paletteIds[index] = colorId(assets, swatch.red, swatch.green, swatch.blue, swatch.alpha)
                end
            end
        end
        for y = math.max(0, origin.y), math.min(sprite.height, origin.y + image.height) - 1 do
            for x = math.max(0, origin.x), math.min(sprite.width, origin.x + image.width) - 1 do
                local value = image:getPixel(x - origin.x, y - origin.y)
                local id, empty
                if image.colorMode == ColorMode.INDEXED then
                    id, empty = paletteIds[value], value == transparent
                else
                    local alpha = pixel.rgbaA(value)
                    id = colorId(assets, pixel.rgbaR(value), pixel.rgbaG(value), pixel.rgbaB(value), alpha)
                    empty = alpha == 0
                end
                if not empty then
                    assert(id, ("Unknown terrain color in '%s' at cell (%d, %d). Use a terrain swatch.")
                        :format(layer.name, x + 1, y + 1))
                    cells[y * sprite.width + x] = id
                    occupied[#occupied + 1] = { x = x, y = y, terrain = id }
                end
            end
        end
    end

    local function sample(x, y)
        if x >= 0 and x < sprite.width and y >= 0 and y < sprite.height then
            return cells[y * sprite.width + x]
        end
    end
    local tiles = {}
    for _, cell in ipairs(occupied) do
        local brush = assets.brushes[cell.terrain]
        if brush.sprites then
            local mask = assets.neighborhood(sample, cell.x, cell.y)
            cell.atlasIndex = assert(brush.sprites[mask],
                ("Missing tile for %s mask %d."):format(cell.terrain, mask))
            tiles[#tiles + 1] = cell
        else
            for _, part in ipairs(brush.parts) do
                local x, y = cell.x + part.x, cell.y + part.y
                if x >= 0 and y >= 0 and x < sprite.width and y < sprite.height then
                    tiles[#tiles + 1] = { x = x, y = y, terrain = cell.terrain, atlasIndex = part.index }
                end
            end
        end
    end
    return { name = layer.name, cells = tiles, anchors = occupied, solid = Map.layerSolid(layer) }
end

function Map.read(assets, sprite, includeHidden)
    local group = assert(Map.group(sprite), "Open a plan with a top-level '" .. profile.planGroup .. "' group.")
    assert(#sprite.frames == 1, "A terrain plan must contain exactly one frame.")
    assert(sprite.colorMode == ColorMode.INDEXED or sprite.colorMode == ColorMode.RGB,
        "Use Indexed or RGB mode for terrain plans.")
    assert(#group.layers > 0, "Add a terrain layer inside the plan group.")
    local result = { width = sprite.width, height = sprite.height, layers = {} }
    for _, layer in ipairs(group.layers) do
        assert(layer.isImage and not layer.isTilemap and not layer.isReference and not layer.isBackground,
            ("'%s' must be a regular image layer directly inside the plan group."):format(layer.name))
        if includeHidden or (group.isVisible and layer.isVisible) then
            result.layers[#result.layers + 1] = decodePlanLayer(assets, sprite, layer)
        end
    end
    return result
end

local function exportLayers(plan, collisionTile)
    local layers = {}
    local collisionLayers = {}
    local names = {}
    for _, layer in ipairs(plan.layers) do
        local base = { name = layer.name, cells = {} }
        if layer.solid ~= nil then base.solid = false end
        local planes, depth = { base }, {}
        layers[#layers + 1] = base
        names[layer.name] = true
        if layer.solid and #layer.anchors > 0 then
            local cells = {}
            for _, anchor in ipairs(layer.anchors) do
                cells[#cells + 1] = { x = anchor.x, y = anchor.y, atlasIndex = collisionTile }
            end
            collisionLayers[#collisionLayers + 1] = { cells = cells, collisionTarget = #layers - 1 }
        end
        -- Keep overlapping prop fragments as editable tiles. Each occupied cell
        -- stacks independently in the same north-to-south order as the preview.
        for _, cell in ipairs(layer.cells) do
            local key = cell.y * plan.width + cell.x
            local index = (depth[key] or 0) + 1
            depth[key] = index
            if not planes[index] then
                planes[index] = { name = ("%s · Overlap %d"):format(layer.name, index), cells = {} }
                names[planes[index].name] = true
                layers[#layers + 1] = planes[index]
            end
            local cells = planes[index].cells
            cells[#cells + 1] = cell
        end
    end
    for _, collision in ipairs(collisionLayers) do
        local base = layers[collision.collisionTarget + 1].name .. " · Solid Cells"
        local name, suffix = base, 2
        while names[name] do
            name = base .. " " .. suffix
            suffix = suffix + 1
        end
        collision.name = name
        names[name] = true
        layers[#layers + 1] = collision
    end
    return layers
end

function Map.export(assets, source, filename)
    assert(app.fs.fileExtension(filename):lower() == "aseprite", "Choose an .aseprite export file.")
    local function comparable(path)
        return app.fs.normalizePath(path):lower()
    end
    assert(comparable(filename) ~= comparable(source.filename), "Choose a separate file for the tilemap export.")
    for _, open in ipairs(app.sprites) do
        assert(comparable(filename) ~= comparable(open.filename),
            "Close the destination document before exporting over it.")
    end
    local plan = Map.read(assets, source, true)
    local tilesize = assets.tileSize
    local maxCellsPerSide = math.min(512, 65535 // tilesize)
    assert(plan.width <= maxCellsPerSide and plan.height <= maxCellsPerSide,
        "The plan is too large for the configured tile size.")

    local previousSprite, previousLayer = app.sprite, app.layer
    local sprite = Sprite(plan.width * tilesize, plan.height * tilesize, ColorMode.RGB)
    local success, failure = pcall(function()
        sprite.gridBounds = Rectangle(0, 0, tilesize, tilesize)
        local defaultLayer = sprite.layers[1]
        local tileset, tileIds
        for _, sourceLayer in ipairs(exportLayers(plan, assets.atlasIndexes[1])) do
            app.command.NewLayer { name = sourceLayer.name, tilemap = true, ui = false }
            local layer = app.layer
            assert(layer.isTilemap and layer.stackIndex > 1, "Could not create a tilemap layer.")
            if sourceLayer.collisionTarget ~= nil then
                layer.data = "AsepriteTilemap:Collision:" .. sourceLayer.collisionTarget
                layer.isVisible = false
            elseif sourceLayer.solid ~= nil then
                layer.data = "AsepriteTilemap:Passable"
            end
            if tileset then
                local unused = layer.tileset
                if unused ~= tileset then
                    layer.tileset = tileset
                    sprite:deleteTileset(unused)
                end
            else
                tileset, tileIds = layer.tileset, {}
                tileset.name = profile.tilesetName
                for _, index in ipairs(assets.atlasIndexes) do
                    local tile = sprite:newTile(tileset)
                    tile.image:drawImage(Image(assets.atlas, assets.rects[index]), Point(0, 0))
                    tileIds[index] = tile.index
                end
            end
            local image = Image(plan.width, plan.height, ColorMode.TILEMAP)
            for _, cell in ipairs(sourceLayer.cells) do
                image:drawPixel(cell.x, cell.y, pixel.tile(tileIds[cell.atlasIndex]))
            end
            sprite:newCel(layer, 1, image, Point(0, 0))
        end
        sprite:deleteLayer(defaultLayer)
        assert(sprite:saveAs(filename), "Aseprite could not save the tilemap.")
    end)
    sprite:close()
    if previousSprite then
        app.sprite = previousSprite
        if previousLayer then app.layer = previousLayer end
    end
    if not success then error(failure, 0) end
end

return Map
