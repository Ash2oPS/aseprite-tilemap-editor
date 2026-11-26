local terrain = {}

function terrain.neighborhood(sample, x, y)
    local center = sample(x, y)
    if center == nil then
        return nil
    end

    local north = sample(x, y - 1) == center
    local east = sample(x + 1, y) == center
    local south = sample(x, y + 1) == center
    local west = sample(x - 1, y) == center
    local mask = (north and 1 or 0) + (east and 2 or 0)
        + (south and 4 or 0) + (west and 8 or 0)

    if north and east and sample(x + 1, y - 1) == center then
        mask = mask + 16
    end
    if south and east and sample(x + 1, y + 1) == center then
        mask = mask + 32
    end
    if south and west and sample(x - 1, y + 1) == center then
        mask = mask + 64
    end
    if north and west and sample(x - 1, y - 1) == center then
        mask = mask + 128
    end

    return mask
end

return terrain
