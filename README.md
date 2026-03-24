# PHD2 API

A N.I.N.A. plugin that exposes PHD2 as a REST API and real-time WebSocket server.

- **REST API:** `http://localhost:{ApiPort}/api/v1/phd2/`
- **WebSocket:** `ws://localhost:{ApiPort}/api/v1/events/`
- **Swagger UI:** `http://localhost:{ApiPort}/api/v1/swagger`
- **OpenAPI spec:** `http://localhost:{ApiPort}/api/v1/openapi.json`

---

## Plugin Settings

| Setting | Default | Description |
|---------|---------|-------------|
| `Phd2Host` | `localhost` | Hostname or IP address of the machine running PHD2 |
| `Phd2Port` | `4400` | PHD2 TCP server port |
| `ApiPort` | `2888` | Port on which this plugin's REST API and WebSocket server listen |

> **Network access:** By default the server binds to `localhost` only. To allow access from other machines run:
> ```
> netsh http add urlacl url=http://+:{ApiPort}/ user=%USERNAME%
> ```

---

## API Response Format

All REST endpoints return JSON in the following envelope:

**Success:**
```json
{ "success": true, "data": { ... } }
```

**Error:**
```json
{ "success": false, "message": "error description" }
```

HTTP `503` is returned when PHD2 is not connected. HTTP `404` is returned for unknown endpoints.

---

## REST Endpoints

### Status

#### `GET /api/v1/phd2/appstate`
Returns the current PHD2 application state.

**Response `data`:**
```json
{ "state": "Guiding" }
```
Possible values: `Stopped` `Selected` `Calibrating` `Guiding` `LostLock` `Paused` `Looping`

---

#### `GET /api/v1/phd2/version`
Returns the PHD2 version string.

**Response `data`:**
```json
{ "version": "2.6.13" }
```

---

#### `GET /api/v1/phd2/connected`
Returns whether all equipment is connected.

**Response `data`:**
```json
{ "connected": true }
```

---

#### `GET /api/v1/phd2/calibrated`
Returns whether PHD2 has valid calibration data.

**Response `data`:**
```json
{ "calibrated": true }
```

---

#### `GET /api/v1/phd2/lockposition`
Returns the current lock position in pixels, or `null` if not set.

**Response `data`:**
```json
{ "lockPosition": [512.0, 384.0] }
```

---

#### `GET /api/v1/phd2/pixelscale`
Returns the guider pixel scale in arcseconds per pixel.

**Response `data`:**
```json
{ "pixelScale": 1.23 }
```

---

#### `GET /api/v1/phd2/searchregion`
Returns the search region radius in pixels.

**Response `data`:**
```json
{ "searchRegion": 15 }
```

---

#### `GET /api/v1/phd2/starimage`
Returns the current guide star image as base64-encoded PNG along with frame metadata.

**Response `data`:**
```json
{
  "frame": 42,
  "width": 15,
  "height": 15,
  "star_pos": [7.5, 7.3],
  "pixels": "<base64-encoded PNG>"
}
```

---

#### `GET /api/v1/phd2/starimage.png[?size=15]`
Returns the guide star image directly as a binary PNG (8-bit grayscale).  
Can be used as an `<img>` `src` or opened directly in a browser.

| Query param | Type | Default | Description |
|-------------|------|---------|-------------|
| `size` | integer | `15` | Image region size in pixels (min 8, max 128) |

**Response:** `image/png` binary

---

### Guiding

#### `GET /api/v1/phd2/exposure`
Returns the current camera exposure time.

**Response `data`:**
```json
{ "exposure": 2000 }
```

---

#### `POST /api/v1/phd2/exposure`
Sets the camera exposure time.

**Request body:**
```json
{ "exposure": 2000 }
```

---

#### `GET /api/v1/phd2/exposuredurations`
Returns the list of valid exposure durations supported by PHD2.

**Response `data`:**
```json
{ "durations": [500, 1000, 2000, 3000, 5000] }
```

---

#### `GET /api/v1/phd2/paused`
Returns whether guiding output is currently paused.

**Response `data`:**
```json
{ "paused": false }
```

---

#### `POST /api/v1/phd2/paused`
Pauses or resumes guiding output.

**Request body:**
```json
{ "paused": true, "full": false }
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `paused` | boolean | ✔ | `true` to pause, `false` to resume |
| `full` | boolean | – | `true` to also pause looping exposures (default: `false`) |

---

#### `GET /api/v1/phd2/guideoutput`
Returns whether guide output pulses are enabled.

**Response `data`:**
```json
{ "enabled": true }
```

---

#### `POST /api/v1/phd2/guideoutput`
Enables or disables guide output pulses.

**Request body:**
```json
{ "enabled": true }
```

---

#### `POST /api/v1/phd2/guide`
Starts guiding. PHD2 will auto-select a star if needed, calibrate if needed, and start guiding.  
A `SettleDone` event is broadcast over WebSocket when settling is complete.

**Request body:**
```json
{
  "settle": { "pixels": 1.5, "time": 8, "timeout": 40 },
  "recalibrate": false
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `settle` | SettleParams | ✔ | Settling criteria (see below) |
| `recalibrate` | boolean | – | Force recalibration before guiding (default: `false`) |

**SettleParams:**

| Field | Type | Description |
|-------|------|-------------|
| `pixels` | number | Max guide distance considered stable (pixels) |
| `time` | number | Minimum seconds to remain within the `pixels` threshold |
| `timeout` | number | Maximum seconds to wait before declaring settle failed |

---

#### `POST /api/v1/phd2/dither`
Randomly shifts the guide lock position. A `SettleDone` event is broadcast when stable.

**Request body:**
```json
{
  "amount": 10,
  "raOnly": false,
  "settle": { "pixels": 1.5, "time": 8, "timeout": 40 }
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `amount` | number | ✔ | Dither magnitude in pixels (multiplied by Dither Scale in PHD2) |
| `raOnly` | boolean | – | Dither on RA axis only (default: `false`) |
| `settle` | SettleParams | ✔ | Settling criteria |

---

#### `POST /api/v1/phd2/loop`
Starts looping exposures without guiding.

---

#### `POST /api/v1/phd2/stopcapture`
Stops all capturing and guiding.

---

#### `POST /api/v1/phd2/findstar`
Instructs PHD2 to auto-select a guide star.

---

#### `POST /api/v1/phd2/guidepulse`
Sends a single manual guide pulse.  
Returns an error if PHD2 is currently calibrating or guiding.

**Request body:**
```json
{ "amount": 200, "direction": "N", "which": "Mount" }
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `amount` | integer | ✔ | Pulse duration in milliseconds |
| `direction` | string | ✔ | `N`, `S`, `E`, or `W` |
| `which` | string | – | Target device: `Mount` or `AO` (default: `Mount`) |

---

### Equipment

#### `POST /api/v1/phd2/connect`
Connects or disconnects all equipment.

**Request body:**
```json
{ "connect": true }
```

---

#### `GET /api/v1/phd2/profile`
Returns the currently active equipment profile.

**Response `data`:** Profile object from PHD2 (id, name, etc.)

---

#### `GET /api/v1/phd2/profiles`
Returns all available equipment profiles.

**Response `data`:** Array of profile objects from PHD2.

---

#### `POST /api/v1/phd2/setprofile`
Switches to a different equipment profile.  
All equipment must be disconnected before switching.

**Request body:**
```json
{ "profileId": 1 }
```

---

#### `GET /api/v1/phd2/equipment`
Returns the currently connected equipment devices as reported by PHD2.

**Response `data`:** Equipment object from PHD2 (camera, mount, etc.)

---

#### `GET /api/v1/phd2/calibrationdata`
Returns the current calibration data from PHD2.

**Response `data`:** Calibration data object from PHD2.

---

#### `POST /api/v1/phd2/flipcalibration`
Flips the calibration data (for meridian flips).

---

#### `POST /api/v1/phd2/clearcalibration`
Clears calibration data.

**Request body (optional):**
```json
{ "which": "both" }
```

| Field | Type | Description |
|-------|------|-------------|
| `which` | string | `mount`, `ao`, or `both` (default: `both`) |

---

### Server

#### `GET /api/v1/phd2/wsclients`
Returns the number of currently connected WebSocket clients.

**Response `data`:**
```json
{ "clients": 2 }
```

---

## WebSocket Events

Connect to `ws://localhost:{ApiPort}/api/v1/events/` to receive real-time events.  
All messages are JSON objects with at minimum:

```json
{
  "Event": "EventName",
  "Timestamp": 1718000000.0,
  "Host": "localhost",
  "Inst": 1
}
```

### Plugin Events

| Event | Additional fields | Description |
|-------|-------------------|-------------|
| `Phd2ApiConnected` | – | Plugin has established connection to PHD2 |
| `Phd2ApiDisconnected` | – | Connection to PHD2 was lost |

### PHD2 Events

| Event | Additional fields | Description |
|-------|-------------------|-------------|
| `Version` | `PHDVersion`, `PHDSubver`, `MsgVersion`, `OverlapSupport` | PHD2 started and announced its version |
| `AppState` | `State` | PHD2 application state changed |
| `GuideStep` | `Frame`, `Time`, `Mount`, `dx`, `dy`, `RADistanceRaw`, `DECDistanceRaw`, `RADistanceGuide`, `DECDistanceGuide`, `RADuration`, `RADirection`, `DECDuration`, `DECDirection`, `StarMass`, `SNR`, `HFD`, `AvgDist`, `RALimited`\*, `DecLimited`\*, `ErrorCode`\* | One completed guide frame |
| `StarLost` | `Frame`, `Time`, `StarMass`, `SNR`, `AvgDist`, `ErrorCode`, `Status` | Guide star was lost |
| `Settling` | `Distance`, `Time`, `SettleTime`, `StarLocked` | Settling progress update |
| `SettleDone` | `Status`, `Error`, `TotalFrames`, `DroppedFrames` | Settling completed (`Status`: 0 = success) |
| `LockPositionSet` | `X`, `Y` | Lock position was changed |
| `StarSelected` | `X`, `Y` | A guide star was selected |
| `StartCalibration` | `Mount` | Calibration started |
| `Calibrating` | `Mount`, `dir`, `dist`, `dx`, `dy`, `step`, `State` | Calibration step progress |
| `CalibrationComplete` | `Mount` | Calibration completed successfully |
| `CalibrationFailed` | `Reason` | Calibration failed |
| `CalibrationDataFlipped` | `Mount` | Calibration data was flipped |
| `GuidingDithered` | `dx`, `dy` | Lock position was dithered |
| `LoopingExposures` | `Frame` | Looping exposure frame completed |
| `Alert` | `Msg`, `Type` | PHD2 alert message |
| `GuideParamChange` | `Name`, `Value` | A guiding parameter was changed |

\* Only present when non-zero/non-default.