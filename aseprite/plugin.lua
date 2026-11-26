local Map, Preview, Picker, assets, preview, root, installedVersion, profile

local function handle(action)
    return function()
        local ok, detail = pcall(action)
        if not ok then
            app.alert { title = profile.menuTitle, text = tostring(detail):gsub("^.-%.lua:%d+: ", "") }
        end
    end
end

local function getAssets()
    if not assets then assets = Map.load(root) end
    return assets
end

local function updatePlugin()
    local fs = app.fs
    local folder = fs.joinPath(fs.userConfigPath, profile.updateFolder)
    local latest, archive = installedVersion, nil
    if fs.isDirectory(folder) then
        for _, name in ipairs(fs.listFiles(folder)) do
            local version = name:match("^" .. profile.archiveStem:gsub("([^%w])", "%%%1") .. "%-(%d+%.%d+%.%d+)%.aseprite%-extension$")
            local path = fs.joinPath(folder, name)
            if version and Version(version) > latest and fs.isFile(path) then
                latest, archive = Version(version), path
            end
        end
    end
    if archive then
        app.command.Options { installExtension = archive }
    else
        app.alert { title = profile.menuTitle, text = { "No newer local update found.", folder } }
    end
end

local runUpdate = handle(updatePlugin)

local function showPreview()
    if preview then return end
    preview = Preview.open(Map, getAssets(), Picker, function() preview = nil end, runUpdate, profile)
end

local function newPlan()
    local dialog = Dialog("New Tilemap Plan")
    dialog:number { id = "width", label = "Width (cells)", text = "16", decimals = 0 }
    dialog:number { id = "height", label = "Height (cells)", text = "11", decimals = 0 }
    dialog:button { id = "create", text = "Create", focus = true }
    dialog:button { text = "Cancel" }
    dialog:show()
    local data = dialog.data
    if data.create then
        Map.newPlan(getAssets(), math.tointeger(data.width), math.tointeger(data.height))
        showPreview()
    end
end

local function exportTilemap()
    local source = app.sprite
    local fs = app.fs
    local folder = fs.filePath(source.filename)
    local name = fs.fileTitle(source.filename) .. "-Tilemap.aseprite"
    local path = folder ~= "" and fs.joinPath(folder, name) or name
    local dialog = Dialog("Export Tilemap")
    dialog:file {
        id = "filename", label = "Destination", title = "Export Tilemap",
        filename = path, save = true, open = false, entry = true, filetypes = { "aseprite" },
    }
    dialog:button { id = "export", text = "Export" }
    dialog:button { text = "Cancel" }
    dialog:show()
    local data = dialog.data
    if not data.export or not data.filename or data.filename == "" then return end
    if fs.filePath(data.filename) == "" then
        error("Choose a destination folder with the file picker.", 0)
    end
    if fs.isFile(data.filename) then
        local choice = app.alert {
            title = "Replace Tilemap?",
            text = fs.fileName(data.filename),
            buttons = { "Replace", "Cancel" },
        }
        if choice ~= 1 then return end
    end
    Map.export(getAssets(), source, data.filename)
    app.alert { title = profile.menuTitle, text = { "Tilemap saved.", data.filename } }
end

function init(plugin)
    root = plugin.path
    installedVersion = Version(tostring(plugin.version))
    Map = dofile(app.fs.joinPath(root, "map.lua"))
    profile = Map.configure(root)
    Preview = dofile(app.fs.joinPath(root, "preview.lua"))
    Picker = dofile(app.fs.joinPath(root, "brush_picker.lua"))
    local group = profile.commandPrefix .. "_menu"
    local function hasPlan() return Map.group(app.sprite) ~= nil end
    plugin:newMenuGroup { id = group, title = profile.menuTitle, group = "file_scripts" }
    plugin:newCommand {
        id = profile.commandPrefix .. "NewPlan", title = "New Terrain Plan...",
        group = group, onclick = handle(newPlan),
    }
    plugin:newCommand {
        id = profile.commandPrefix .. "Preview", title = "Show Tile Preview",
        group = group, onclick = handle(showPreview), onenabled = hasPlan,
    }
    plugin:newCommand {
        id = profile.commandPrefix .. "Swatches", title = profile.swatchesTitle or "Add Pack Swatches",
        group = group, onenabled = hasPlan,
        onclick = handle(function() Map.addSwatches(getAssets(), app.sprite) end),
    }
    plugin:newCommand {
        id = profile.commandPrefix .. "Export", title = "Export Tilemap...",
        group = group, onclick = handle(exportTilemap), onenabled = hasPlan,
    }
    plugin:newCommand {
        id = profile.commandPrefix .. "Update", title = "Update Plugin...",
        group = group, onclick = runUpdate,
    }
end

function exit()
    if preview then preview:close() end
end
