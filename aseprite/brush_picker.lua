local Picker = {}

local rowHeight, tileWidth, thumbSize = 64, 57, 38
local panelColor = Color { r = 32, g = 36, b = 42 }
local brushColor = Color { r = 46, g = 52, b = 59 }
local selectedColor = Color { r = 72, g = 103, b = 79 }
local textColor = Color { r = 239, g = 242, b = 240 }
local scrollColor = Color { r = 124, g = 140, b = 144 }

local function boundsOf(parts)
    local left, right, top, bottom = 0, 0, 0, 0
    for _, part in ipairs(parts) do
        left, right = math.min(left, part.x), math.max(right, part.x)
        top, bottom = math.min(top, part.y), math.max(bottom, part.y)
    end
    return left, top, right - left + 1, bottom - top + 1
end

local function thumbnail(assets, brush)
    local parts = brush.parts or {
        { x = 0, y = 0, index = brush.sprites[brush.thumbnailMask] or brush.sprites[0] },
    }
    local left, top, width, height = boundsOf(parts)
    local size = assets.rects[parts[1].index].width
    local image = Image(width * size, height * size, ColorMode.RGB)
    for _, part in ipairs(parts) do
        image:drawImage(Image(assets.atlas, assets.rects[part.index]),
            Point((part.x - left) * size, (part.y - top) * size))
    end
    local scale = thumbSize / math.max(image.width, image.height)
    image:resize(math.max(1, math.floor(image.width * scale)), math.max(1, math.floor(image.height * scale)))
    return image
end

local function drawLabel(gc, label, left, y, width)
    local first, second = "", ""
    for word in label:gmatch("%S+") do
        local nextLine = first == "" and word or first .. " " .. word
        if second == "" and gc:measureText(nextLine).width <= width - 6 then
            first = nextLine
        else
            second = second == "" and word or second .. " " .. word
        end
    end
    for index, line in ipairs { first, second } do
        local original = line
        while #line > 0 and gc:measureText(line).width > width - 6 do
            line = line:sub(1, -2)
        end
        if line ~= original then
            while #line > 0 and gc:measureText(line .. "...").width > width - 6 do
                line = line:sub(1, -2)
            end
            line = line .. "..."
        end
        local measured = gc:measureText(line)
        gc:fillText(line, left + (width - measured.width) // 2, y + (index - 1) * 10)
    end
end

function Picker.attach(dialog, Map, assets, profile)
    local categories, seenCategories = { "All" }, { All = true }
    local selected, currentSprite
    local caption, footprint = "Choose a brush", nil
    local visible, labels, thumbnails = {}, {}, {}
    local columns, cellWidth = 1, tileWidth
    local scroll, maxScroll, viewportHeight, viewportWidth = 0, 0, 0, 0
    for _, id in ipairs(assets.brushOrder) do
        local brush = assets.brushes[id]
        labels[id] = brush.label:gsub("(%l)(%u)", "%1 %2")
        if not seenCategories[brush.category] then
            seenCategories[brush.category] = true
            categories[#categories + 1] = brush.category
        end
    end

    local function filter(category, text)
        visible, scroll = {}, 0
        local query = text:lower()
        for _, id in ipairs(assets.brushOrder) do
            local brush = assets.brushes[id]
            if (category == "All" or brush.category == category)
                and (labels[id]:lower():find(query, 1, true) or id:lower():find(query, 1, true)) then
                visible[#visible + 1] = id
            end
        end
    end

    local function status(id, message)
        caption = message or "Choose a brush"
        footprint = nil
        if id then
            local brush = assets.brushes[id]
            caption = labels[id]
            if brush.parts then
                local _, _, width, height = boundsOf(brush.parts)
                local anchor = brush.anchor
                footprint = ("%s · %d × %d cells"):format(anchor, width, height)
            end
        end
    end

    local function draw(event)
        local gc = event.context
        viewportHeight, viewportWidth = gc.height, gc.width
        gc.color = panelColor
        gc:fillRect(Rectangle(0, 0, gc.width, gc.height))
        columns = math.max(1, (gc.width - 7) // tileWidth)
        cellWidth = math.max(1, (gc.width - 7) // columns)
        local height = ((#visible + columns - 1) // columns) * rowHeight
        maxScroll = math.max(0, height - gc.height)
        scroll = math.min(scroll, maxScroll)
        if #visible == 0 then
            gc.color = textColor
            gc:fillText("No matching brushes.", 7, 7)
            return
        end
        local start = (scroll // rowHeight) * columns + 1
        local stop = math.min(#visible, ((scroll + gc.height + rowHeight - 1) // rowHeight) * columns)
        for index = start, stop do
            local id = visible[index]
            local x = ((index - 1) % columns) * cellWidth
            local y = ((index - 1) // columns) * rowHeight - scroll
            gc.color = id == selected and selectedColor or brushColor
            gc:fillRect(Rectangle(x + 1, y + 1, cellWidth - 2, rowHeight - 2))
            if not thumbnails[id] then thumbnails[id] = thumbnail(assets, assets.brushes[id]) end
            local image = thumbnails[id]
            gc:drawImage(image, x + (cellWidth - image.width) // 2, y + 2 + (thumbSize - image.height) // 2)
            gc.color = textColor
            drawLabel(gc, labels[id], x, y + 41, cellWidth)
        end
        if maxScroll > 0 then
            local grip = math.max(12, gc.height * gc.height // height)
            gc.color = scrollColor
            gc:fillRect(Rectangle(gc.width - 5, (gc.height - grip) * scroll // maxScroll, 3, grip))
        end
    end

    local function filterChanged()
        filter(dialog.data.brushCategory, dialog.data.brushSearch)
        dialog:repaint()
    end

    local initialCategory = seenCategories[profile.defaultCategory] and profile.defaultCategory or "All"
    dialog:newrow()
    dialog:separator { text = "Brush Palette" }
    dialog:entry { id = "brushSearch", label = "Search", text = "", onchange = filterChanged }
    dialog:combobox { id = "brushCategory", label = "Category", options = categories, option = initialCategory,
        onchange = filterChanged }
    dialog:canvas {
        id = "brushGrid", width = 180, height = 136, hexpand = true, onpaint = draw,
        onwheel = function(event)
            scroll = math.max(0, math.min(maxScroll, scroll + event.deltaY * rowHeight))
            dialog:repaint()
        end,
        onmousedown = function(event)
            if event.button ~= MouseButton.LEFT then return end
            if event.x >= columns * cellWidth then
                if event.x >= viewportWidth - 6 and maxScroll > 0 then
                    scroll = math.max(0, math.min(maxScroll, math.floor(event.y / viewportHeight * maxScroll)))
                    dialog:repaint()
                end
                return
            end
            local id = visible[((event.y + scroll) // rowHeight) * columns + event.x // cellWidth + 1]
            if not id then return end
            local sprite = app.sprite
            if not Map.group(sprite) then return end
            local ok, result = pcall(Map.brushColor, assets, sprite, id)
            if not ok then
                status(nil, tostring(result):gsub("^.-%.lua:%d+: ", ""))
                dialog:repaint()
                return
            end
            app.fgColor = result
            selected, currentSprite = id, sprite
            status(id)
            dialog:repaint()
        end,
    }
    dialog:canvas {
        id = "brushSelection", width = 180, height = 24, hexpand = true,
        onpaint = function(event)
            local gc = event.context
            gc.color = panelColor
            gc:fillRect(Rectangle(0, 0, gc.width, gc.height))
            local left = 4
            if selected then
                gc.color = assets.brushes[selected].swatch
                gc:fillRect(Rectangle(left, 3, 8, 8))
                left = left + 12
            end
            gc.color = textColor
            gc:fillText(caption, left, 2)
            if footprint then gc:fillText(footprint, left, 12) end
        end,
    }
    filter(initialCategory, "")
    return function()
        local sprite = app.sprite
        local id
        if Map.group(sprite) then
            local color = app.fgColor
            if sprite.colorMode == ColorMode.INDEXED then
                local index, palette = color.index, sprite.palettes[1]
                color = index >= 0 and index < #palette and index ~= sprite.transparentColor
                    and palette:getColor(index) or nil
            end
            if color then id = Map.brushId(assets, color) end
        end
        if sprite ~= currentSprite or id ~= selected then
            currentSprite, selected = sprite, id
            status(id, not Map.group(sprite) and "Open a terrain plan" or nil)
            dialog:repaint()
        end
    end
end

return Picker
