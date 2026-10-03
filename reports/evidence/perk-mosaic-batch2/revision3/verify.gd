extends SceneTree
func _initialize():
    var manifest = JSON.parse_string(FileAccess.get_file_as_string("C:/Projects/festival-tycoon/assets/source/perks/mosaic-suite-v1/manifest.json"))
    var checks = []
    for card in manifest.cards:
        var prefix = "res://assets/ui/perks/" + card.id
        var texture = load(prefix + ".png") as Texture2D
        var fallback = load(prefix + ".svg") as Texture2D
        if texture == null or fallback == null or texture.get_width() != 1536 or texture.get_height() != 1024:
            push_error("Perk artwork import failed: " + card.id)
            quit(2)
            return
        checks.append({"id":card.id,"width":texture.get_width(),"height":texture.get_height(),"pngLoaded":true,"svgLoaded":true})
    var output = FileAccess.open("C:/Projects/festival-tycoon/artifacts/perk-mosaic-batch2-r3/native-imports.json",FileAccess.WRITE)
    output.store_string(JSON.stringify({"passed":true,"checks":checks,"scope":"Native texture loading only; batch review cards are supplied previews, not in-game screenshots."},"  "))
    print("PERK_MOSAIC_IMPORTS_OK count=" + str(checks.size()))
    quit(0)

