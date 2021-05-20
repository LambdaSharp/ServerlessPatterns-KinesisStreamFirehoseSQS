using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Amazon.Lambda.KinesisEvents;
using Amazon.S3;
using Amazon.S3.Model;
using LambdaSharp;

namespace ServerlessPatterns.KinesisStream.StoreFunction {

    public sealed class Function : ALambdaFunction<KinesisEvent, string> {

        //--- Fields ---
        private string _bucketName;
        private IAmazonS3 _s3Client;

        //--- Constructors ---
        public Function() : base(new LambdaSharp.Serialization.LambdaSystemTextJsonSerializer()) { }

        //--- Methods ---
        public override async Task InitializeAsync(LambdaConfig config) {

            // read configuration
            _bucketName = config.ReadS3BucketName("BucketArn");

            // initialize clients
            _s3Client = new AmazonS3Client();
        }

        public override async Task<string> ProcessMessageAsync(KinesisEvent request) {
            LogInfo($"Received {request.Records.Count:N0} Kinesis Stream records");

            // combine all records and compress them for storage
            var originalSize = request.Records.Sum(record => record.Kinesis.Data.Length);
            using var eventStream = new MemoryStream();
            using(var gzipStream = new GZipStream(eventStream, CompressionLevel.Optimal, leaveOpen: true)) {
                foreach(var record in request.Records) {
                    record.Kinesis.Data.CopyTo(gzipStream);
                    gzipStream.WriteByte((byte)'\n');
                }
            }
            eventStream.Position = 0;
            var compressedSize = eventStream.Length;
            LogInfo($"Compressed {request.Records.Count:N0} Kinesis Stream records (original: {originalSize:N0} bytes, stored: {compressedSize:N0} bytes)");

            // store event in S3
            var arrivalTime = request.Records.First().Kinesis.ApproximateArrivalTimestamp;
            var s3Key = $"{arrivalTime.Year:0000}/{arrivalTime.Month:00}/{arrivalTime.Hour:00}/{request.Records.First().EventId.Split(':', 2)[1]}.gz";
            await _s3Client.PutObjectAsync(new PutObjectRequest {
                BucketName = _bucketName,
                Key = s3Key,
                InputStream = eventStream
            });
            LogInfo($"Stored {s3Key}");
            return "Ok";
        }
    }
}
