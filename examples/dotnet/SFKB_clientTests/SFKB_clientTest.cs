using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using SFKB_API;

namespace SFKB_clientTests
{
    [TestClass]
    public class SfkbClientTest
    {
        private const string ConfigLocation = @"../../../config.json";
        private static Client Client;
        private Guid DatasetId;
        private static readonly Guid WrongDatasetId = new Guid("2fa85f64-5717-4562-b3fc-2c963f66afa6");
        private const string Ar5DatasetName = "ar5_test_23";
        private const string Ar5FlateFeatureLokalId = "20f893f2-5c8c-466f-b25b-d51ae98f1399";
        private const string Ar5GrenseFeatureLokalId = "0003f094-b524-4a5a-bb05-d69881df853a";

        [TestInitialize]
        public void Init()
        {
            var basicIdentification = File.Exists(ConfigLocation)
                ? GetIdentificationFromConfigFile()
                : GetIdentificationFromEnvironmentVariables();

            var byteArray = Encoding.ASCII.GetBytes(basicIdentification);

            var httpClient = new HttpClient();

            var basicValue = Convert.ToBase64String(byteArray);

            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basicValue);

            Client = new Client(httpClient);

            GetDatasets();
        }

        private static string GetIdentificationFromEnvironmentVariables()
        {
            return $"{Environment.GetEnvironmentVariable("api_user")}:{Environment.GetEnvironmentVariable("api_pass")}";
        }

        private static string GetIdentificationFromConfigFile()
        {
            string basicIdentification;
            var config = JObject.Parse(File.ReadAllText(ConfigLocation));

            basicIdentification = $"{config["user"].ToString()}:{config["password"].ToString()}";
            return basicIdentification;
        }

        [TestMethod]
        public void TestGetDatasets()
        {
            Assert.IsTrue(GetDataset().Id == DatasetId, $"No dataset found with id {DatasetId}");
        }

        private Dataset GetDataset()
        {
            return Client.GetDatasetMetadataAsync(DatasetId).Result;
        }

        private void GetDatasets()
        {
            var datasets = Client.GetDatasetsAsync().Result;

            Assert.IsTrue(datasets.Count > 0, "No datasets returned");

            DatasetId = datasets.FirstOrDefault( d => d.Name == Ar5DatasetName).Id;
        }

        [TestMethod]
        public void TestGetDatasetWithWrongId()
        {
            Exception exception = null;
            try
            {
                var result = Client.GetDatasetMetadataAsync(WrongDatasetId).Result;
            }
            catch (Exception e)
            {
                exception = e;
            }
            finally
            {
                var httpStatusIs403 = exception?.InnerException?.Message?.Contains("403") ?? false;

                Assert.IsTrue(httpStatusIs403, $"Wrong result when asking for non-existing Dataset");
            }
        }

        [TestMethod]
        public void TestReplacePolygonFeature()
        {
            ReplaceByLokalId(Ar5FlateFeatureLokalId);
        }

        [TestMethod]
        public void TestReplaceReferencedLineFeature()
        {
            ReplaceByLokalId(Ar5GrenseFeatureLokalId);
        }

        private void ReplaceByLokalId(string lokalId)
        {
            var locking = GetLocking();

            var tempFile = LockAndSaveFeatureByLokalId(lokalId, locking);

            var lockedLokalIds = Client.GetDatasetLocksAsync(DatasetId, locking).Result.SelectMany(l => l.Features.Select(f => f.Lokalid)).ToList();

            var wfsReplaceFile = Wfs.CreateReplaceWrappingForFeatures(tempFile, lockedLokalIds);

            using (var featureStream = File.OpenRead(wfsReplaceFile))
            {
                var result = Client.UpdateDatasetFeaturesAsync(DatasetId, locking, featureStream).Result;

                Assert.IsTrue(result.Features_replaced > 0, "No features updated");
            };

            Client.DeleteDatasetLocksAsync(DatasetId, locking);
        }

        //[TestMethod]
        //public void TestGetFeaturesWithBbox()
        //{
        //    var fileResponse = Client.GetDatasetFeaturesAsync(DatasetId, null, GetExampleBbox(), null).Result;

        //    WriteStreamToDisk(fileResponse);
        //}

        private static string WriteStreamToDisk(FileResponse fileResponse)
        {
            var tempFile = Path.GetTempFileName();

            using (var fileStream = File.OpenWrite(tempFile))
            {
                fileResponse.Stream.CopyTo(fileStream);
            }

            Assert.IsTrue(File.Exists(tempFile), "No features saved to disk");

            var fileInfo = new FileInfo(tempFile);

            Assert.IsTrue(fileInfo.Length > 0, "Empty file saved to disk");
            
            return tempFile;
        }

        [TestMethod]
        public void TestGetLockedFeaturesByLokalId()
        {
            var locking = GetLocking();

            LockAndSaveFeatureByLokalId(Ar5FlateFeatureLokalId, locking);

            Client.DeleteDatasetLocksAsync(DatasetId, locking);

            var locks = Client.GetDatasetLocksAsync(DatasetId, locking).Result.FirstOrDefault();

            Assert.IsTrue(locks == null, "Locks not deleted");
        }

        private string LockAndSaveFeatureByLokalId(string lokalId, Locking locking)
        {
            var fileResponse = Client.GetDatasetFeaturesAsync(DatasetId, locking, null, GetExampleQuery(lokalId)).Result;

            return WriteStreamToDisk(fileResponse);
        }

        [TestMethod]
        public void TestGetFeaturesByLokalId()
        {
            LockAndSaveFeatureByLokalId(Ar5GrenseFeatureLokalId, null);
        }

        private string GetExampleQuery(string ar5GrenseFeatureLokalId)
        {
            return $"eq(*/identifikasjon/lokalid,{Ar5FlateFeatureLokalId})";
        }

        //private BoundingBox GetExampleBbox()
        //{
        //    var ll1 = 365600;
        //    var ll2 = 7217500;
        //    var ur1 = 366100;
        //    var ur2 = 7217850;

        //    return new BoundingBox { Ll = new List<double> { ll1,  ll2 }, Ur = new List<double> { ur1, ur2 } };
        //}

        //private Stream GetExampleFeatureStream(string fileName)
        //{
        //    Assert.IsTrue(File.Exists(fileName), $"Example feature not found at {fileName}");

        //    return new StreamReader(fileName).BaseStream;
        //}

        private static Locking GetLocking()
        {
            return new Locking { Type = LockingType.User_lock };
        }
    }
}
