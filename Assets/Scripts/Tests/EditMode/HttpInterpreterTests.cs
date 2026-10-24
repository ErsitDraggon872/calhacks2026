using NUnit.Framework;
using UnityEngine;

namespace CalHacks.Tests
{
    public class HttpInterpreterTests
    {
        // A reply exactly as tools/thinkink_backend.py sends it: identity plus Andy's eight-field components and
        // numeric attributes (which Unity doesn't read yet and JsonUtility skips).
        const string BackendApple = @"{""status"":""ok"",""name"":""apple"",""message"":""An apple!"",""adjectives"":[""red""],
            ""material"":""plant"",""traits"":[""edible""],""width_m"":0.8,
            ""components"":[{""type"":""support"",""activation"":""always"",""anchor_x"":0.5,""anchor_y"":0.5,""launch_speed"":0,
                ""angle_deg"":0,""acceleration"":0,""duration_s"":0}],
            ""attributes"":{""mass_kg"":0.3,""friction"":0.6},
            ""capture_id"":""cam-4"",""run_id"":3,""provider"":""bitdeer"",""ink_cost"":9,""latency_ms"":2140}";

        static BoardCapture Capture() => new BoardCapture
        {
            Request = new CaptureRequest { socket_id = "gatekeeper", run_id = 3, goal = "something the gatekeeper will accept" },
            Cutout = new CaptureResponse { id = "cam-4", png_base64 = "CUTOUT", ink_px = 1234 },
            BoardWidth = 1280,
            BoardHeight = 720,
            CutoutRect = new RectInt(400, 160, 390, 330),
        };

        [Test]
        public void RequestCarriesTheContractFields()
        {
            var json = JsonUtility.ToJson(HttpInterpreter.BuildRequest(Capture(), "BOARD"));
            var back = JsonUtility.FromJson<InterpretRequest>(json);
            Assert.AreEqual("cam-4", back.capture_id);
            Assert.AreEqual(3, back.run_id);
            Assert.AreEqual("gatekeeper", back.socket_id);
            Assert.AreEqual("something the gatekeeper will accept", back.goal);
            Assert.AreEqual("BOARD", back.board_png_base64);
            Assert.AreEqual("CUTOUT", back.cutout_png_base64);
            Assert.AreEqual((1280, 720), (back.board_width, back.board_height));
            Assert.AreEqual((400, 160, 390, 330), (back.cutout_x, back.cutout_y, back.cutout_width, back.cutout_height));
            Assert.AreEqual(1234, back.ink_px);
            StringAssert.Contains("\"board_png_base64\":", json);
        }

        [Test]
        public void BackendReplyBecomesAnEdibleSpec()
        {
            var reply = JsonUtility.FromJson<InterpretResponse>(BackendApple);
            var label = HttpInterpreter.ToLabel(reply, Capture().Request);
            Assert.IsTrue(label.Ok);
            Assert.AreEqual("apple", label.Data.label);
            Assert.AreEqual(3, label.Data.run_id);
            Assert.AreEqual("bitdeer", label.Data.provider);
            var spec = InventionSpec.Parse(label.Data.interpretation);
            Assert.AreEqual("apple", spec.Name);
            Assert.IsTrue(spec.Has(Trait.Edible));
            Assert.AreEqual(0, spec.Dropped.Count);
        }

        [Test]
        public void ErrorOrEmptyReplyIsAFailureNotUnknown()
        {
            var request = Capture().Request;
            var error = HttpInterpreter.ToLabel(new InterpretResponse { status = "error", message = "AI unavailable. Try again." }, request);
            Assert.IsFalse(error.Ok);
            StringAssert.Contains("AI unavailable", error.Error);
            Assert.IsFalse(HttpInterpreter.ToLabel(new InterpretResponse(), request).Ok);
            Assert.IsFalse(HttpInterpreter.ToLabel(null, request).Ok);
        }

        [Test]
        public void RejectedReplyIsAnAnswer()
        {
            var label = HttpInterpreter.ToLabel(new InterpretResponse { status = "rejected", name = "", message = "Blank board." }, Capture().Request);
            Assert.IsTrue(label.Ok);
            Assert.AreEqual(SocketLogic.Unknown, label.Data.label);
            Assert.IsTrue(InventionSpec.Parse(label.Data.interpretation).Rejected);
        }
    }
}
