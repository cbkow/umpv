(function(thisObj) {
    var myPanel = (thisObj instanceof Panel) ? thisObj : new Window("palette", "Import UnionMpvPlayer Notes", undefined, { resizeable: true });

    function buildUI(thisPanel) {
        thisPanel.orientation = "column";
        thisPanel.alignChildren = ["fill", "top"];
        thisPanel.spacing = 10;
        thisPanel.margins = 10;

        // === File Selection Panel ===
        var fileGroup = thisPanel.add("panel", undefined, "Select JSON File");
        fileGroup.orientation = "column";
        fileGroup.alignChildren = ["fill", "top"];
        fileGroup.margins = 10;

        var filePathText = fileGroup.add("edittext", undefined, "");
        filePathText.preferredSize.width = 100;

        var browseButton = fileGroup.add("button", undefined, "Browse...");

        // === Options Panel ===
        var optionsGroup = thisPanel.add("panel", undefined, "Import Options");
        optionsGroup.orientation = "column";
        optionsGroup.alignChildren = ["left", "top"];
        optionsGroup.margins = 10;

        var importMarkers = optionsGroup.add("checkbox", undefined, "Import Markers");
        importMarkers.value = true;

        var importStills = optionsGroup.add("checkbox", undefined, "Import Still Images");
        importStills.value = true;

        var stillDuration = optionsGroup.add("group");
        stillDuration.orientation = "row";
        stillDuration.alignChildren = ["left", "center"];
        stillDuration.add("statictext", undefined, "Still Duration (frames):");
        var stillDurationInput = stillDuration.add("edittext", undefined, "10");
        stillDurationInput.preferredSize.width = 50;

        // === Button Group ===
        var buttonGroup = thisPanel.add("group");
        buttonGroup.orientation = "row";
        buttonGroup.alignment = "right";

        var okButton = buttonGroup.add("button", undefined, "Import");
        okButton.enabled = false;

        // === Browse logic ===
        browseButton.onClick = function () {
            var file = File.openDialog("Select Notes JSON File", "JSON Files:*.json");
            if (file) {
                filePathText.text = file.fsName;
                okButton.enabled = true;
            }
        };

        // === Drag-and-drop support ===
        filePathText.addEventListener("dragenter", function(event) {
            event.preventDefault();
        });

        filePathText.addEventListener("dragover", function(event) {
            event.preventDefault();
        });

        filePathText.addEventListener("drop", function(event) {
            var droppedFile = event.dataTransfer.files[0];
            if (droppedFile && droppedFile.name.match(/\.json$/i)) {
                filePathText.text = droppedFile.path;
                okButton.enabled = true;
            } else {
                alert("Please drop a valid JSON file.");
            }
        });

        // === Import button logic ===
        okButton.onClick = function () {
            try {
                importNotesFromJson(filePathText.text, {
                    importMarkers: importMarkers.value,
                    importStills: importStills.value,
                    stillDuration: parseInt(stillDurationInput.text, 10) || 10
                });
            } catch (e) {
                alert("Error importing notes: " + e.toString());
            }
        };

        return thisPanel;
    }

    var builtPanel = buildUI(myPanel);

    if (builtPanel instanceof Window) {
        builtPanel.center();
        builtPanel.show();
    } else {
        builtPanel.layout.layout(true);
        builtPanel.layout.resize();
    }

    // === Main Import Logic ===
    function importNotesFromJson(jsonPath, options) {
        var file = new File(jsonPath);
        file.open("r");
        var jsonContent = file.read();
        file.close();

        var data = JSON.parse(jsonContent);
        var projectInfo = data.ProjectInfo;
        var notesData = data.Notes;

        var comp = app.project.activeItem;
        if (!(comp instanceof CompItem)) {
            comp = app.project.items.addComp(
                projectInfo.VideoName,
                projectInfo.Width,
                projectInfo.Height,
                1,
                projectInfo.Duration,
                projectInfo.Fps
            );
        } else {
            if (comp.duration < projectInfo.Duration) {
                comp.duration = projectInfo.Duration;
            }
        }

        var videoItem = null;
        try {
            var videoFile = new File(projectInfo.VideoPath);
            if (videoFile.exists) {
                var importOptions = new ImportOptions(videoFile);
                videoItem = app.project.importFile(importOptions);
                comp.layers.add(videoItem);
            }
        } catch (e) {
            alert("Could not import video: " + e.toString());
        }

        for (var i = 0; i < notesData.length; i++) {
            var noteItem = notesData[i];
            var frameTime = noteItem.FrameNumber / projectInfo.Fps;

            if (options.importMarkers) {
                var markerObj = new MarkerValue(noteItem.Notes);
                comp.markerProperty.setValueAtTime(frameTime, markerObj);
            }

            if (options.importStills) {
                try {
                    // Use the exported image path directly from the JSON
                    var imgPath = noteItem.ExportedImagePath;
                    
                    var imgFile = new File(imgPath);
                    if (imgFile.exists) {
                        var imgOptions = new ImportOptions(imgFile);
                        var img = app.project.importFile(imgOptions);
                        var imgLayer = comp.layers.add(img);

                        imgLayer.startTime = frameTime;
                        imgLayer.outPoint = frameTime + (options.stillDuration / projectInfo.Fps);
                    }
                } catch (e) {
                    alert("Error importing image for note " + (i+1) + ": " + e.toString());
                }
            }
        }

        alert("Import complete! Imported " + notesData.length + " notes.");
    }
})(this);