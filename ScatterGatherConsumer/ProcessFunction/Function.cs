using System.Threading.Tasks;
using Amazon.Lambda.SNSEvents;
using LambdaSharp;
using LambdaSharp.SimpleQueueService;

namespace ServerlessPatterns.ScatterGatherConsumer.ProcessFunction {

    public class Event {

        //--- Properties ---
        public string Id { get; set; }
        public string EventType { get; set; }
        public string Message { get; set; }
    }

    public sealed class Function : ALambdaQueueFunction<SNSEvent.SNSMessage> {

        //--- Constructors ---
        public Function() : base(new LambdaSharp.Serialization.LambdaSystemTextJsonSerializer()) { }

        //--- Methods ---
        public override async Task InitializeAsync(LambdaConfig config) { }

        public override async Task ProcessMessageAsync(SNSEvent.SNSMessage sns) {
            var evt = LambdaSerializer.Deserialize<Event>(sns.Message);
            LogInfo($"Received {evt.EventType}: {evt.Message}");
        }
    }
}
