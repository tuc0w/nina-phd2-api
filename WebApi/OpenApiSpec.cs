using System.Text.Json;
using System.Text.Json.Nodes;

namespace AndreasBehrend.NINA.Phd2Api.WebApi {

    internal static class OpenApiSpec {

        public static string Generate(int port) {
            var spec = new JsonObject {
                ["openapi"] = "3.0.3",
                ["info"] = new JsonObject {
                    ["title"] = "PHD2 API",
                    ["description"] = "REST API for the PHD2 autoguider via N.I.N.A. plugin.\n\n" +
                        "Real-time events are pushed over WebSocket at `ws://localhost:" + port + "/api/v1/events/`.\n\n" +
                        "**AppState values:** `Stopped` `Selected` `Calibrating` `Guiding` `LostLock` `Paused` `Looping`",
                    ["version"] = "1.0.0",
                    ["contact"] = new JsonObject {
                        ["name"] = "Andreas Behrend"
                    }
                },
                ["servers"] = new JsonArray {
                    new JsonObject {
                        ["url"] = "http://localhost:" + port + "/api/v1",
                        ["description"] = "PHD2 API (local)"
                    }
                },
                ["tags"] = new JsonArray {
                    MakeTag("Status",      "PHD2 state and sensor queries"),
                    MakeTag("Guiding",     "Guiding, looping and dithering"),
                    MakeTag("Equipment",   "Equipment, profiles and calibration"),
                    MakeTag("Server",      "API server information")
                },
                ["paths"] = BuildPaths(),
                ["components"] = new JsonObject {
                    ["schemas"] = BuildSchemas()
                }
            };

            return spec.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }

        // ── helpers ───────────────────────────────────────────────────────────

        private static JsonObject MakeTag(string name, string description) =>
            new JsonObject { ["name"] = name, ["description"] = description };

        private static JsonArray Tags(params string[] tags) {
            var arr = new JsonArray();
            foreach (var t in tags) arr.Add((JsonNode)t);
            return arr;
        }

        private static JsonObject OkResponses(JsonNode dataSchema = null) {
            var responseSchema = new JsonObject {
                ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["success"] = new JsonObject { ["type"] = "boolean" },
                    ["data"] = dataSchema ?? new JsonObject { ["type"] = "object", ["nullable"] = true }
                }
            };
            return new JsonObject {
                ["200"] = new JsonObject {
                    ["description"] = "Success",
                    ["content"] = new JsonObject {
                        ["application/json"] = new JsonObject { ["schema"] = responseSchema }
                    }
                },
                ["503"] = new JsonObject {
                    ["description"] = "PHD2 not connected",
                    ["content"] = new JsonObject {
                        ["application/json"] = new JsonObject {
                            ["schema"] = Ref("ErrorResponse")
                        }
                    }
                }
            };
        }

        private static JsonObject RequestBody(JsonNode schema) =>
            new JsonObject {
                ["required"] = true,
                ["content"] = new JsonObject {
                    ["application/json"] = new JsonObject { ["schema"] = schema }
                }
            };

        private static JsonObject MakeGetOp(string summary, string operationId, string[] tags,
            JsonNode dataSchema = null, string description = null) {
            var op = new JsonObject {
                ["summary"] = summary,
                ["operationId"] = operationId,
                ["tags"] = Tags(tags),
                ["responses"] = OkResponses(dataSchema)
            };
            if (description != null) op["description"] = description;
            return op;
        }

        private static JsonObject MakePostOp(string summary, string operationId, string[] tags,
            JsonNode requestSchema = null, string description = null) {
            var op = new JsonObject {
                ["summary"] = summary,
                ["operationId"] = operationId,
                ["tags"] = Tags(tags),
                ["responses"] = OkResponses()
            };
            if (description != null) op["description"] = description;
            if (requestSchema != null) op["requestBody"] = RequestBody(requestSchema);
            return op;
        }

        private static JsonObject MakePngGetOp(string summary, string operationId, string[] tags,
            string description = null) {
            var op = new JsonObject {
                ["summary"]     = summary,
                ["operationId"] = operationId,
                ["tags"]        = Tags(tags),
                ["parameters"]  = new JsonArray {
                    new JsonObject {
                        ["name"]        = "size",
                        ["in"]          = "query",
                        ["required"]    = false,
                        ["description"] = "Star image region size in pixels (default: 15)",
                        ["schema"]      = new JsonObject { ["type"] = "integer", ["default"] = 15, ["minimum"] = 8, ["maximum"] = 128 }
                    }
                },
                ["responses"] = new JsonObject {
                    ["200"] = new JsonObject {
                        ["description"] = "PNG image of the current guide star",
                        ["content"] = new JsonObject {
                            ["image/png"] = new JsonObject {
                                ["schema"] = new JsonObject { ["type"] = "string", ["format"] = "binary" }
                            }
                        }
                    },
                    ["503"] = new JsonObject {
                        ["description"] = "PHD2 not connected or no star selected",
                        ["content"] = new JsonObject {
                            ["application/json"] = new JsonObject { ["schema"] = Ref("ErrorResponse") }
                        }
                    }
                }
            };
            if (description != null) op["description"] = description;
            return op;
        }

        // schema property shorthands
        private static JsonObject Str(string desc)  => new JsonObject { ["type"] = "string",  ["description"] = desc };
        private static JsonObject Num(string desc)  => new JsonObject { ["type"] = "number",  ["description"] = desc };
        private static JsonObject Int(string desc)  => new JsonObject { ["type"] = "integer", ["description"] = desc };
        private static JsonObject Bool(string desc) => new JsonObject { ["type"] = "boolean", ["description"] = desc };
        private static JsonObject Ref(string name)  => new JsonObject { ["$ref"] = "#/components/schemas/" + name };

        // ── paths ─────────────────────────────────────────────────────────────

        private static JsonObject BuildPaths() => new JsonObject {

            // ── Status ────────────────────────────────────────────────────────
            ["/phd2/appstate"] = new JsonObject {
                ["get"] = MakeGetOp("Get PHD2 app state", "getAppState", ["Status"],
                    new JsonObject {
                        ["type"] = "object",
                        ["properties"] = new JsonObject {
                            ["state"] = Str("Current app state: Stopped, Selected, Calibrating, Guiding, LostLock, Paused or Looping")
                        }
                    })
            },
            ["/phd2/version"] = new JsonObject {
                ["get"] = MakeGetOp("Get PHD2 version", "getVersion", ["Status"],
                    new JsonObject {
                        ["type"] = "object",
                        ["properties"] = new JsonObject { ["version"] = Str("PHD2 version string") }
                    })
            },
            ["/phd2/connected"] = new JsonObject {
                ["get"] = MakeGetOp("Get equipment connection status", "getConnected", ["Status"],
                    new JsonObject {
                        ["type"] = "object",
                        ["properties"] = new JsonObject { ["connected"] = Bool("True if all equipment is connected") }
                    })
            },
            ["/phd2/calibrated"] = new JsonObject {
                ["get"] = MakeGetOp("Get calibration status", "getCalibrated", ["Status"],
                    new JsonObject {
                        ["type"] = "object",
                        ["properties"] = new JsonObject { ["calibrated"] = Bool("True if PHD2 is calibrated") }
                    })
            },
            ["/phd2/lockposition"] = new JsonObject {
                ["get"] = MakeGetOp("Get lock position", "getLockPosition", ["Status"],
                    new JsonObject {
                        ["type"] = "object",
                        ["properties"] = new JsonObject {
                            ["lockPosition"] = new JsonObject {
                                ["type"] = "array",
                                ["nullable"] = true,
                                ["items"] = new JsonObject { ["type"] = "number" },
                                ["description"] = "[x, y] pixel coordinates, or null if not set"
                            }
                        }
                    })
            },
            ["/phd2/pixelscale"] = new JsonObject {
                ["get"] = MakeGetOp("Get guider pixel scale", "getPixelScale", ["Status"],
                    new JsonObject {
                        ["type"] = "object",
                        ["properties"] = new JsonObject { ["pixelScale"] = Num("Arcsec per pixel") }
                    })
            },
            ["/phd2/searchregion"] = new JsonObject {
                ["get"] = MakeGetOp("Get search region radius", "getSearchRegion", ["Status"],
                    new JsonObject {
                        ["type"] = "object",
                        ["properties"] = new JsonObject { ["searchRegion"] = Int("Search region radius in pixels") }
                    })
            },
            ["/phd2/starimage"] = new JsonObject {
                ["get"] = MakeGetOp("Get current star image (JSON/base64)", "getStarImage", ["Status"],
                    description: "Returns frame metadata and raw 8-bit grayscale pixel data as a base64 string. " +
                        "For direct browser display use `GET /phd2/starimage.png` instead.")
            },
            ["/phd2/starimage.png"] = new JsonObject {
                ["get"] = MakePngGetOp("Get current star image as PNG", "getStarImagePng", ["Status"],
                    description: "Returns a PNG-encoded 8-bit grayscale image of the guide star. " +
                        "Can be used directly as an `<img>` src or opened in any browser. " +
                        "Use the optional `size` parameter to control the image region size.")
            },

            // ── Guiding ───────────────────────────────────────────────────────
            ["/phd2/exposure"] = new JsonObject {
                ["get"] = MakeGetOp("Get camera exposure time", "getExposure", ["Guiding"],
                    new JsonObject {
                        ["type"] = "object",
                        ["properties"] = new JsonObject { ["exposure"] = Int("Exposure time in milliseconds") }
                    }),
                ["post"] = MakePostOp("Set camera exposure time", "setExposure", ["Guiding"],
                    Ref("SetExposureRequest"))
            },
            ["/phd2/exposuredurations"] = new JsonObject {
                ["get"] = MakeGetOp("Get valid exposure durations", "getExposureDurations", ["Guiding"],
                    new JsonObject {
                        ["type"] = "object",
                        ["properties"] = new JsonObject {
                            ["durations"] = new JsonObject {
                                ["type"] = "array",
                                ["items"] = new JsonObject { ["type"] = "integer" },
                                ["description"] = "Valid exposure times in milliseconds"
                            }
                        }
                    })
            },
            ["/phd2/paused"] = new JsonObject {
                ["get"] = MakeGetOp("Get paused state", "getPaused", ["Guiding"],
                    new JsonObject {
                        ["type"] = "object",
                        ["properties"] = new JsonObject { ["paused"] = Bool("True if guiding output is paused") }
                    }),
                ["post"] = MakePostOp("Set paused state", "setPaused", ["Guiding"],
                    Ref("SetPausedRequest"),
                    "Pass `full: true` to also pause looping exposures.")
            },
            ["/phd2/guideoutput"] = new JsonObject {
                ["get"] = MakeGetOp("Get guide output enabled state", "getGuideOutput", ["Guiding"],
                    new JsonObject {
                        ["type"] = "object",
                        ["properties"] = new JsonObject { ["enabled"] = Bool("True if guide output is enabled") }
                    }),
                ["post"] = MakePostOp("Set guide output enabled state", "setGuideOutput", ["Guiding"],
                    Ref("SetGuideOutputRequest"))
            },
            ["/phd2/guide"] = new JsonObject {
                ["post"] = MakePostOp("Start guiding", "startGuide", ["Guiding"],
                    Ref("GuideRequest"),
                    "PHD2 will auto-select a star if needed, calibrate if needed, start guiding, " +
                    "and report settling progress. A `SettleDone` event is sent over WebSocket when complete.")
            },
            ["/phd2/dither"] = new JsonObject {
                ["post"] = MakePostOp("Dither the lock position", "dither", ["Guiding"],
                    Ref("DitherRequest"),
                    "Randomly shifts the lock position by ±amount pixels. A `SettleDone` event is sent when stable.")
            },
            ["/phd2/loop"] = new JsonObject {
                ["post"] = MakePostOp("Start looping exposures", "loop", ["Guiding"])
            },
            ["/phd2/stopcapture"] = new JsonObject {
                ["post"] = MakePostOp("Stop capturing and guiding", "stopCapture", ["Guiding"])
            },
            ["/phd2/findstar"] = new JsonObject {
                ["post"] = MakePostOp("Auto-select a guide star", "findStar", ["Guiding"])
            },
            ["/phd2/guidepulse"] = new JsonObject {
                ["post"] = MakePostOp("Send a manual guide pulse", "guidePulse", ["Guiding"],
                    Ref("GuidePulseRequest"),
                    "Returns an error if PHD2 is currently calibrating or guiding.")
            },

            // ── Equipment ─────────────────────────────────────────────────────
            ["/phd2/connect"] = new JsonObject {
                ["post"] = MakePostOp("Connect or disconnect all equipment", "setConnected", ["Equipment"],
                    Ref("SetConnectedRequest"))
            },
            ["/phd2/profile"] = new JsonObject {
                ["get"] = MakeGetOp("Get current equipment profile", "getProfile", ["Equipment"])
            },
            ["/phd2/profiles"] = new JsonObject {
                ["get"] = MakeGetOp("Get all equipment profiles", "getProfiles", ["Equipment"])
            },
            ["/phd2/setprofile"] = new JsonObject {
                ["post"] = MakePostOp("Switch equipment profile", "setProfile", ["Equipment"],
                    Ref("SetProfileRequest"),
                    "All equipment must be disconnected before switching profiles.")
            },
            ["/phd2/equipment"] = new JsonObject {
                ["get"] = MakeGetOp("Get current equipment devices", "getEquipment", ["Equipment"])
            },
            ["/phd2/calibrationdata"] = new JsonObject {
                ["get"] = MakeGetOp("Get calibration data", "getCalibrationData", ["Equipment"])
            },
            ["/phd2/flipcalibration"] = new JsonObject {
                ["post"] = MakePostOp("Flip calibration data", "flipCalibration", ["Equipment"])
            },
            ["/phd2/clearcalibration"] = new JsonObject {
                ["post"] = MakePostOp("Clear calibration data", "clearCalibration", ["Equipment"],
                    Ref("ClearCalibrationRequest"))
            },

            // ── Server ────────────────────────────────────────────────────────
            ["/phd2/wsclients"] = new JsonObject {
                ["get"] = MakeGetOp("Get connected WebSocket client count", "getWsClients", ["Server"],
                    new JsonObject {
                        ["type"] = "object",
                        ["properties"] = new JsonObject { ["clients"] = Int("Number of active WebSocket connections") }
                    })
            }
        };

        // ── schemas ───────────────────────────────────────────────────────────

        private static JsonObject BuildSchemas() => new JsonObject {

            ["ErrorResponse"] = new JsonObject {
                ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["success"] = new JsonObject { ["type"] = "boolean", ["example"] = false },
                    ["message"] = Str("Error description")
                }
            },

            ["SettleParams"] = new JsonObject {
                ["type"] = "object",
                ["required"] = new JsonArray { "pixels", "time", "timeout" },
                ["properties"] = new JsonObject {
                    ["pixels"]  = Num("Max guide distance considered stable (pixels)"),
                    ["time"]    = Num("Min seconds to remain within pixels threshold"),
                    ["timeout"] = Num("Max seconds to wait before declaring settle failed")
                },
                ["example"] = new JsonObject { ["pixels"] = 1.5, ["time"] = 8, ["timeout"] = 40 }
            },

            ["GuideRequest"] = new JsonObject {
                ["type"] = "object",
                ["required"] = new JsonArray { "settle" },
                ["properties"] = new JsonObject {
                    ["settle"]      = Ref("SettleParams"),
                    ["recalibrate"] = Bool("Force recalibration before guiding (default: false)")
                },
                ["example"] = new JsonObject {
                    ["settle"] = new JsonObject { ["pixels"] = 1.5, ["time"] = 8, ["timeout"] = 40 },
                    ["recalibrate"] = false
                }
            },

            ["DitherRequest"] = new JsonObject {
                ["type"] = "object",
                ["required"] = new JsonArray { "amount", "settle" },
                ["properties"] = new JsonObject {
                    ["amount"] = Num("Dither magnitude in pixels (multiplied by Dither Scale in PHD2)"),
                    ["raOnly"] = Bool("Dither only on the RA axis (default: false)"),
                    ["settle"] = Ref("SettleParams")
                },
                ["example"] = new JsonObject {
                    ["amount"] = 10,
                    ["raOnly"] = false,
                    ["settle"] = new JsonObject { ["pixels"] = 1.5, ["time"] = 8, ["timeout"] = 40 }
                }
            },

            ["SetExposureRequest"] = new JsonObject {
                ["type"] = "object",
                ["required"] = new JsonArray { "exposure" },
                ["properties"] = new JsonObject {
                    ["exposure"] = Int("Exposure time in milliseconds")
                },
                ["example"] = new JsonObject { ["exposure"] = 2000 }
            },

            ["SetPausedRequest"] = new JsonObject {
                ["type"] = "object",
                ["required"] = new JsonArray { "paused" },
                ["properties"] = new JsonObject {
                    ["paused"] = Bool("True to pause, false to resume"),
                    ["full"]   = Bool("True to also pause looping exposures (default: false)")
                },
                ["example"] = new JsonObject { ["paused"] = true, ["full"] = false }
            },

            ["SetConnectedRequest"] = new JsonObject {
                ["type"] = "object",
                ["required"] = new JsonArray { "connect" },
                ["properties"] = new JsonObject {
                    ["connect"] = Bool("True to connect equipment, false to disconnect")
                },
                ["example"] = new JsonObject { ["connect"] = true }
            },

            ["SetGuideOutputRequest"] = new JsonObject {
                ["type"] = "object",
                ["required"] = new JsonArray { "enabled" },
                ["properties"] = new JsonObject {
                    ["enabled"] = Bool("True to enable guide output")
                },
                ["example"] = new JsonObject { ["enabled"] = true }
            },

            ["SetProfileRequest"] = new JsonObject {
                ["type"] = "object",
                ["required"] = new JsonArray { "profileId" },
                ["properties"] = new JsonObject {
                    ["profileId"] = Int("Equipment profile ID (from GET /phd2/profiles)")
                },
                ["example"] = new JsonObject { ["profileId"] = 1 }
            },

            ["ClearCalibrationRequest"] = new JsonObject {
                ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["which"] = new JsonObject {
                        ["type"] = "string",
                        ["enum"] = new JsonArray { "mount", "ao", "both" },
                        ["description"] = "Which calibration to clear (default: both)"
                    }
                },
                ["example"] = new JsonObject { ["which"] = "both" }
            },

            ["GuidePulseRequest"] = new JsonObject {
                ["type"] = "object",
                ["required"] = new JsonArray { "amount", "direction" },
                ["properties"] = new JsonObject {
                    ["amount"]    = Int("Pulse duration in milliseconds"),
                    ["direction"] = new JsonObject {
                        ["type"] = "string",
                        ["enum"] = new JsonArray { "N", "S", "E", "W" },
                        ["description"] = "Guide direction"
                    },
                    ["which"] = new JsonObject {
                        ["type"] = "string",
                        ["enum"] = new JsonArray { "Mount", "AO" },
                        ["description"] = "Target device (default: Mount)"
                    }
                },
                ["example"] = new JsonObject { ["amount"] = 200, ["direction"] = "N", ["which"] = "Mount" }
            }
        };
    }
}
