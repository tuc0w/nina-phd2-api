using System.Text.Json;
using System.Text.Json.Serialization;

namespace AndreasBehrend.NINA.Phd2Api.Phd2 {

    public class Phd2EventBase {
        [JsonPropertyName("Event")]
        public string Event { get; set; } = string.Empty;
        [JsonPropertyName("Timestamp")]
        public double Timestamp { get; set; }
        [JsonPropertyName("Host")]
        public string Host { get; set; } = string.Empty;
        [JsonPropertyName("Inst")]
        public int Inst { get; set; }
    }

    public class VersionEvent : Phd2EventBase {
        [JsonPropertyName("PHDVersion")]
        public string PHDVersion { get; set; } = string.Empty;
        [JsonPropertyName("PHDSubver")]
        public string PHDSubver { get; set; } = string.Empty;
        [JsonPropertyName("MsgVersion")]
        public int MsgVersion { get; set; }
        [JsonPropertyName("OverlapSupport")]
        public bool OverlapSupport { get; set; }
    }

    public class AppStateEvent : Phd2EventBase {
        [JsonPropertyName("State")]
        public string State { get; set; } = string.Empty;
    }

    public class GuideStepEvent : Phd2EventBase {
        [JsonPropertyName("Frame")]
        public int Frame { get; set; }
        [JsonPropertyName("Time")]
        public double Time { get; set; }
        [JsonPropertyName("Mount")]
        public string Mount { get; set; } = string.Empty;
        [JsonPropertyName("dx")]
        public double Dx { get; set; }
        [JsonPropertyName("dy")]
        public double Dy { get; set; }
        [JsonPropertyName("RADistanceRaw")]
        public double RADistanceRaw { get; set; }
        [JsonPropertyName("DECDistanceRaw")]
        public double DECDistanceRaw { get; set; }
        [JsonPropertyName("RADistanceGuide")]
        public double RADistanceGuide { get; set; }
        [JsonPropertyName("DECDistanceGuide")]
        public double DECDistanceGuide { get; set; }
        [JsonPropertyName("RADuration")]
        public double RADuration { get; set; }
        [JsonPropertyName("RADirection")]
        public string RADirection { get; set; } = string.Empty;
        [JsonPropertyName("DECDuration")]
        public double DECDuration { get; set; }
        [JsonPropertyName("DECDirection")]
        public string DECDirection { get; set; } = string.Empty;
        [JsonPropertyName("StarMass")]
        public double StarMass { get; set; }
        [JsonPropertyName("SNR")]
        public double SNR { get; set; }
        [JsonPropertyName("HFD")]
        public double HFD { get; set; }
        [JsonPropertyName("AvgDist")]
        public double AvgDist { get; set; }
        [JsonPropertyName("RALimited")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool RALimited { get; set; }
        [JsonPropertyName("DecLimited")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool DecLimited { get; set; }
        [JsonPropertyName("ErrorCode")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int ErrorCode { get; set; }
    }

    public class StarLostEvent : Phd2EventBase {
        [JsonPropertyName("Frame")]
        public int Frame { get; set; }
        [JsonPropertyName("Time")]
        public double Time { get; set; }
        [JsonPropertyName("StarMass")]
        public double StarMass { get; set; }
        [JsonPropertyName("SNR")]
        public double SNR { get; set; }
        [JsonPropertyName("AvgDist")]
        public double AvgDist { get; set; }
        [JsonPropertyName("ErrorCode")]
        public int ErrorCode { get; set; }
        [JsonPropertyName("Status")]
        public string Status { get; set; } = string.Empty;
    }

    public class SettlingEvent : Phd2EventBase {
        [JsonPropertyName("Distance")]
        public double Distance { get; set; }
        [JsonPropertyName("Time")]
        public double Time { get; set; }
        [JsonPropertyName("SettleTime")]
        public double SettleTime { get; set; }
        [JsonPropertyName("StarLocked")]
        public bool StarLocked { get; set; }
    }

    public class SettleDoneEvent : Phd2EventBase {
        [JsonPropertyName("Status")]
        public int Status { get; set; }
        [JsonPropertyName("Error")]
        public string Error { get; set; } = string.Empty;
        [JsonPropertyName("TotalFrames")]
        public int TotalFrames { get; set; }
        [JsonPropertyName("DroppedFrames")]
        public int DroppedFrames { get; set; }
    }

    public class LockPositionSetEvent : Phd2EventBase {
        [JsonPropertyName("X")]
        public double X { get; set; }
        [JsonPropertyName("Y")]
        public double Y { get; set; }
    }

    public class StarSelectedEvent : Phd2EventBase {
        [JsonPropertyName("X")]
        public double X { get; set; }
        [JsonPropertyName("Y")]
        public double Y { get; set; }
    }

    public class CalibratingEvent : Phd2EventBase {
        [JsonPropertyName("Mount")]
        public string Mount { get; set; } = string.Empty;
        [JsonPropertyName("dir")]
        public string Dir { get; set; } = string.Empty;
        [JsonPropertyName("dist")]
        public double Dist { get; set; }
        [JsonPropertyName("dx")]
        public double Dx { get; set; }
        [JsonPropertyName("dy")]
        public double Dy { get; set; }
        [JsonPropertyName("step")]
        public int Step { get; set; }
        [JsonPropertyName("State")]
        public string State { get; set; } = string.Empty;
    }

    public class CalibrationCompleteEvent : Phd2EventBase {
        [JsonPropertyName("Mount")]
        public string Mount { get; set; } = string.Empty;
    }

    public class CalibrationFailedEvent : Phd2EventBase {
        [JsonPropertyName("Reason")]
        public string Reason { get; set; } = string.Empty;
    }

    public class CalibrationDataFlippedEvent : Phd2EventBase {
        [JsonPropertyName("Mount")]
        public string Mount { get; set; } = string.Empty;
    }

    public class StartCalibrationEvent : Phd2EventBase {
        [JsonPropertyName("Mount")]
        public string Mount { get; set; } = string.Empty;
    }

    public class GuidingDitheredEvent : Phd2EventBase {
        [JsonPropertyName("dx")]
        public double Dx { get; set; }
        [JsonPropertyName("dy")]
        public double Dy { get; set; }
    }

    public class AlertEvent : Phd2EventBase {
        [JsonPropertyName("Msg")]
        public string Msg { get; set; } = string.Empty;
        [JsonPropertyName("Type")]
        public string Type { get; set; } = string.Empty;
    }

    public class GuideParamChangeEvent : Phd2EventBase {
        [JsonPropertyName("Name")]
        public string Name { get; set; } = string.Empty;
        [JsonPropertyName("Value")]
        public JsonElement Value { get; set; }
    }

    public class LoopingExposuresEvent : Phd2EventBase {
        [JsonPropertyName("Frame")]
        public int Frame { get; set; }
    }
}
