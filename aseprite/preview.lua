local Preview = {}
local background = Color { r = 32, g = 36, b = 42 }
local emptyCell = Color { r = 51, g = 56, b = 63 }
local foreground = Color { r = 224, g = 228, b = 234 }
local locatorOutline = Color { r = 255, g = 213, b = 83 }

function Preview.open(Map, assets, Picker, onclose, onUpdate, profile)
    local sprite, spriteEvents, siteEvent, timer
    local listeners = {}
    local dirty = true
    local snapshot, problem
    local zoom, panX, panY = 1, 0, 0
    local mouseX, mouseY
    local frame, view
    local tileScale, scaledTiles
    local locatorImage
    local solidEnabled, solidSelected

    local function invalidate() dirty = true end

    local function detach()
        if spriteEvents then
            -- Aseprite destroys Sprite.events when its document closes.
            for _, open in ipairs(app.sprites) do
                if open == sprite then
                    for _, listener in ipairs(listeners) do spriteEvents:off(listener) end
                    break
                end
            end
        end
        sprite, spriteEvents, listeners = nil, nil, {}
    end

    local dialog = Dialog {
        title = profile.menuTitle .. " - Preview",
        resizeable = true,
        onclose = function()
            if timer then timer:stop() end
            if siteEvent then app.events:off(siteEvent) end
            detach()
            onclose()
        end,
    }
    local function resetView()
        zoom, panX, panY = 1, 0, 0
    end

    local function refresh()
        if not dirty then return end
        dirty = false
        local active = app.sprite
        if active ~= sprite then
            detach()
            sprite = active
            resetView()
            if Map.group(active) then
                spriteEvents = active.events
                for _, event in ipairs { "change", "layervisibility", "layername" } do
                    listeners[#listeners + 1] = spriteEvents:on(event, invalidate)
                end
            end
        end
        if Map.group(active) then
            local ok, result = pcall(Map.read, assets, active, false)
            snapshot = ok and result or nil
            problem = not ok and tostring(result):gsub("^.-%.lua:%d+: ", "") or nil
        else
            snapshot, problem = nil, "Open a tilemap plan."
        end
        frame = nil
        locatorImage = nil
        dialog:repaint()
    end

    local function drawLocator(canvas, width, height, left, top, scale)
        local mapWidth, mapHeight = snapshot.width, snapshot.height
        if width < 145 or height < 95 or (left >= 0 and top >= 0
            and left + mapWidth * scale <= width and top + mapHeight * scale <= height) then return end
        if not locatorImage then
            locatorImage = Image(mapWidth, mapHeight, ColorMode.RGB)
            locatorImage:clear(emptyCell)
            for _, layer in ipairs(snapshot.layers) do
                for _, anchor in ipairs(layer.anchors or {}) do
                    locatorImage:drawPixel(anchor.x, anchor.y, assets.brushes[anchor.terrain].swatch.rgbaPixel)
                end
            end
        end
        local fit = math.min((width / 3) / mapWidth, (height / 3) / mapHeight, 3)
        local w, h = math.max(1, math.floor(mapWidth * fit)), math.max(1, math.floor(mapHeight * fit))
        local x, y = width - w - 8, 8
        canvas.color = background
        canvas:fillRect(Rectangle(x - 2, y - 2, w + 4, h + 4))
        canvas:drawImage(locatorImage, locatorImage.bounds, Rectangle(x, y, w, h))
        local x1 = math.max(0, math.floor(-left * w / (scale * mapWidth)))
        local y1 = math.max(0, math.floor(-top * h / (scale * mapHeight)))
        local x2 = math.min(w, math.ceil((width - left) * w / (scale * mapWidth)))
        local y2 = math.min(h, math.ceil((height - top) * h / (scale * mapHeight)))
        if x2 > x1 and y2 > y1 then
            canvas.color = locatorOutline
            canvas:strokeRect(Rectangle(x + x1, y + y1, x2 - x1, y2 - y1))
        end
    end

    local function scaledTile(index, width, height)
        local key = width .. "x" .. height
        local tiles = scaledTiles[key]
        if not tiles then
            tiles = {}
            scaledTiles[key] = tiles
        end
        local tile = tiles[index]
        if not tile then
            local source = Image(assets.atlas, assets.rects[index])
            tile = Image(width, height, ColorMode.RGB)
            tile.context:drawImage(source, source.bounds, tile.bounds)
            tiles[index] = tile
        end
        return tile
    end

    local function compose(width, height)
        local scale = math.min((width - 12) / snapshot.width, (height - 12) / snapshot.height, assets.tileSize) * zoom
        if scale <= 0 then return end
        local left = (width - snapshot.width * scale) / 2 + panX
        local top = (height - snapshot.height * scale) / 2 + panY
        if scale ~= tileScale then
            tileScale, scaledTiles = scale, {}
        end

        -- Drawing the atlas through GraphicsContext for each cell makes every paint
        -- depend on the size of the full atlas. Blit pre-sized tiles into one viewport.
        local image = Image(width, height, ColorMode.RGB)
        local canvas = image.context
        canvas.color = background
        canvas:fillRect(image.bounds)
        local mapLeft, mapTop = math.floor(left), math.floor(top)
        canvas.color = emptyCell
        canvas:fillRect(Rectangle(mapLeft, mapTop,
            math.ceil(left + snapshot.width * scale) - mapLeft,
            math.ceil(top + snapshot.height * scale) - mapTop))
        for _, layer in ipairs(snapshot.layers) do
            for _, cell in ipairs(layer.cells) do
                local x1 = math.floor(left + cell.x * scale)
                local y1 = math.floor(top + cell.y * scale)
                local x2 = math.floor(left + (cell.x + 1) * scale)
                local y2 = math.floor(top + (cell.y + 1) * scale)
                if x2 > math.max(x1, 0) and y2 > math.max(y1, 0) and x1 < width and y1 < height then
                    image:drawImage(scaledTile(cell.atlasIndex, x2 - x1, y2 - y1), Point(x1, y1))
                end
            end
        end

        drawLocator(canvas, width, height, left, top, scale)
        return image
    end

    local function draw(event)
        local gc = event.context
        if not snapshot then
            gc.color = background
            gc:fillRect(Rectangle(0, 0, gc.width, gc.height))
            gc.color = foreground
            local x, y = 8, 8
            local line = ""
            for word in (problem or ""):gmatch("%S+") do
                local nextLine = line == "" and word or line .. " " .. word
                if line ~= "" and gc:measureText(nextLine).width > gc.width - 16 then
                    gc:fillText(line, x, y)
                    line, y = word, y + gc:measureText(word).height + 2
                else
                    line = nextLine
                end
            end
            gc:fillText(line, x, y)
            return
        end
        local width, height = gc.width, gc.height
        if not frame or view.width ~= width or view.height ~= height
            or view.zoom ~= zoom or view.panX ~= panX or view.panY ~= panY then
            frame = compose(width, height)
            if not frame then
                gc.color = background
                gc:fillRect(Rectangle(0, 0, width, height))
                return
            end
            view = { width = width, height = height, zoom = zoom, panX = panX, panY = panY }
        end
        gc:drawImage(frame, 0, 0)
    end

    dialog:canvas {
        id = "preview", width = 180, height = 104, vexpand = true, onpaint = draw,
        onwheel = function(ev)
            if ev.deltaY == 0 or not snapshot or not view then return end
            local nextZoom = math.max(0.5, math.min(12, zoom * (ev.deltaY < 0 and 1.25 or 0.8)))
            if nextZoom == zoom then return end
            local ratio = nextZoom / zoom
            local x, y = ev.x - view.width / 2, ev.y - view.height / 2
            -- Scale around the cursor instead of the center of the preview.
            panX = x + (panX - x) * ratio
            panY = y + (panY - y) * ratio
            zoom = nextZoom
            dialog:repaint()
        end,
        onmousedown = function(ev) mouseX, mouseY = ev.x, ev.y end,
        onmouseup = function() mouseX, mouseY = nil, nil end,
        onmousemove = function(ev)
            if not mouseX then return end
            panX, panY = panX + ev.x - mouseX, panY + ev.y - mouseY
            mouseX, mouseY = ev.x, ev.y
            dialog:repaint()
        end,
    }
    dialog:button { text = "Fit", onclick = function()
        resetView()
        dialog:repaint()
    end }
    dialog:button { text = "Detach", onclick = function()
        if not app.preferences.experimental.multiple_windows then
            app.preferences.experimental.multiple_windows = true
            app.alert { title = "Detach Tilemap Preview",
                text = { "Multiple windows are enabled.", "Save your work, restart Aseprite, then drag the preview title bar." } }
        else
            app.alert { title = "Detach Tilemap Preview",
                text = { "Drag the preview by its title bar.", "If it is still confined, restart Aseprite to apply the window setting." } }
        end
    end }
    if onUpdate then dialog:button { text = "Update Plugin...", onclick = onUpdate } end
    dialog:button { text = "Close" }
    local function selectedMapLayer()
        local layer, group = app.layer, Map.group(app.sprite)
        if layer and group and layer.parent == group and layer.isImage
            and not layer.isTilemap and not layer.isBackground and not layer.isReference then
            return layer
        end
    end
    dialog:newrow()
    dialog:check { id = "solidLayer", text = "Selected layer is solid in Unity", enabled = false,
        onclick = function()
            local layer = selectedMapLayer()
            if layer then Map.setLayerSolid(layer, dialog.data.solidLayer == true) end
        end,
    }
    local syncBrush = Picker.attach(dialog, Map, assets, profile)
    local function syncSolid()
        local layer = selectedMapLayer()
        local enabled, selected = layer ~= nil, layer ~= nil and Map.layerSolid(layer) == true
        if enabled ~= solidEnabled or selected ~= solidSelected then
            solidEnabled, solidSelected = enabled, selected
            dialog:modify { id = "solidLayer", enabled = enabled, selected = selected }
        end
    end

    local window = app.window
    local minimum = dialog.sizeHint
    local width = math.max(minimum.width, math.floor(window.width * 0.40))
    local height = math.max(minimum.height, math.floor(window.height * 0.83))
    local left = math.max(0, window.width - width - 36)
    local editor = app.editor
    if editor and app.sprite and not app.preferences.experimental.multiple_windows then
        local plan = app.sprite
        editor.zoom = math.min(left * 0.75 / plan.width, height * 0.75 / plan.height)
        editor.scroll = {
            x = plan.width / 2 + (window.width - left) / (2 * editor.zoom),
            y = plan.height / 2,
        }
    end
    dialog:show {
        wait = false,
        bounds = Rectangle(left,
            math.max(0, (window.height - height) // 2), width, height),
        autoscrollbars = true,
    }
    siteEvent = app.events:on("sitechange", invalidate)
    timer = Timer { interval = 0.08, ontick = function()
        refresh()
        syncBrush()
        syncSolid()
    end }
    timer:start()
    refresh()
    syncBrush()
    syncSolid()
    return dialog
end

return Preview
