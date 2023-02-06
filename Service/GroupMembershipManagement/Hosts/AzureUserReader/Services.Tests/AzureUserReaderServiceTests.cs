// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using DIConcreteTypes;
using Entities;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Models.Entities;
using Repositories.Contracts;
using Services.Contracts;
using Services.Entities.CustomExceptions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public class AzureUserReaderServiceTests
    {
        [TestInitialize]
        public void SetupTest()
        {
        }

        [TestMethod]
        public async Task GetPersonnelNumbersTest()
        {
            var storageAccountSecret = new StorageAccountSecret("myconnectionstring");
            var loggerMock = new Mock<ILoggingRepository>();
            var blobClientFactoryMock = new Mock<IBlobClientFactory>();
            var blobClientMock = new Mock<BlobClient>();

            var personnelNumber = "1223344";
            var sw = new StreamWriter(new MemoryStream());
            sw.WriteLine("PersonnelNumbers");
            sw.WriteLine(personnelNumber);
            sw.Flush();
            sw.BaseStream.Position = 0;

            var bdi = BlobsModelFactory.BlobDownloadInfo(
                DateTimeOffset.Now,
                1,
                BlobType.Block,
                null, null,
                null,
                null,
                null,
                new Uri("http://file.url"),
                CopyStatus.Success,
                null,
                LeaseDurationType.Fixed,
                null,
                LeaseState.Available,
                null,
                LeaseStatus.Unlocked,
                new byte[0],
                null,
                ETag.All,
                0,
                null,
                false,
                null,
                null,
                1000,
                new byte[0],
                null,
                sw.BaseStream,
                DateTimeOffset.Now);

            var response = new Mock<Response>();
            response.SetupGet(x => x.Status).Returns((int)HttpStatusCode.OK);

            var bdiresponse = Response.FromValue<BlobDownloadInfo>(bdi, null);

            var downloadResponse = new Mock<Response<BlobDownloadInfo>>();
            downloadResponse.SetupGet(x => x.Value).Returns(bdiresponse);
            downloadResponse.Setup(x => x.GetRawResponse()).Returns(response.Object);

            blobClientMock.Setup(x => x.Exists(It.IsAny<CancellationToken>())).Returns(Response.FromValue<bool>(true, new Mock<Response>().Object));
            blobClientMock.Setup(x => x.DownloadAsync()).ReturnsAsync(downloadResponse.Object);

            blobClientFactoryMock.Setup(x => x.GetBlobClient(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(blobClientMock.Object);

            var service = new AzureUserReaderService(storageAccountSecret, loggerMock.Object, blobClientFactoryMock.Object);
            var personnelNumbers = await service.GetPersonnelNumbersAsync("validcontainer", "valid/blob/path/file.csv");

            Assert.AreEqual(1, personnelNumbers.Count);
            Assert.AreEqual(personnelNumbers[0], personnelNumber);
        }

        [TestMethod]
        [ExpectedException(typeof(FileNotFoundException))]
        public async Task GetPersonnelNumbersFileNotFoundTest()
        {
            var storageAccountSecret = new StorageAccountSecret("myconnectionstring");
            var loggerMock = new Mock<ILoggingRepository>();
            var blobClientFactoryMock = new Mock<IBlobClientFactory>();
            var blobClientMock = new Mock<BlobClient>();
            var response = new Mock<Response>();

            blobClientMock.Setup(x => x.Exists(It.IsAny<CancellationToken>())).Returns(Response.FromValue<bool>(false, new Mock<Response>().Object));
            blobClientFactoryMock.Setup(x => x.GetBlobClient(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(blobClientMock.Object);

            var service = new AzureUserReaderService(storageAccountSecret, loggerMock.Object, blobClientFactoryMock.Object);
            var personnelNumbers = await service.GetPersonnelNumbersAsync("validcontainer", "notvalid/blob/path/file.csv");
        }

        [TestMethod]
        [ExpectedException(typeof(DownloadFileException))]
        public async Task GetPersonnelNumbersExceptionTest()
        {
            var storageAccountSecret = new StorageAccountSecret("myconnectionstring");
            var loggerMock = new Mock<ILoggingRepository>();
            var blobClientFactoryMock = new Mock<IBlobClientFactory>();
            var blobClientMock = new Mock<BlobClient>();

            var response = new Mock<Response>();
            response.SetupGet(x => x.Status).Returns((int)HttpStatusCode.RequestTimeout);

            var downloadResponse = new Mock<Response<BlobDownloadInfo>>();
            downloadResponse.Setup(x => x.GetRawResponse()).Returns(response.Object);

            blobClientMock.Setup(x => x.Exists(It.IsAny<CancellationToken>())).Returns(Response.FromValue<bool>(true, new Mock<Response>().Object));
            blobClientMock.Setup(x => x.DownloadAsync()).ReturnsAsync(downloadResponse.Object);
            blobClientFactoryMock.Setup(x => x.GetBlobClient(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(blobClientMock.Object);

            var service = new AzureUserReaderService(storageAccountSecret, loggerMock.Object, blobClientFactoryMock.Object);
            var personnelNumbers = await service.GetPersonnelNumbersAsync("validcontainer", "valid/blob/path/file.csv");
        }

        [TestMethod]
        public async Task UploadUsersMemberIdTest()
        {
            var storageAccountSecret = new StorageAccountSecret("myconnectionstring");
            var loggerMock = new Mock<ILoggingRepository>();
            var blobClientFactoryMock = new Mock<IBlobClientFactory>();
            var blobClientMock = new Mock<BlobClient>();

            var personnelNumbers = new[] { "111111", "222222", "333333", "444444" };
            var sw = new StreamWriter(new MemoryStream());
            sw.WriteLine("PersonnelNumber,AzureObjectId,UserPrincipalName");
            sw.WriteLine($"{personnelNumbers[0]},{Guid.NewGuid()},user1@domain.com");
            sw.WriteLine($"{personnelNumbers[1]},{Guid.NewGuid()},user2@domain.com");
            sw.Flush();
            sw.BaseStream.Position = 0;

            var bdi = BlobsModelFactory.BlobDownloadInfo(
                DateTimeOffset.Now,
                1,
                BlobType.Block,
                null, null,
                null,
                null,
                null,
                new Uri("http://file.url"),
                CopyStatus.Success,
                null,
                LeaseDurationType.Fixed,
                null,
                LeaseState.Available,
                null,
                LeaseStatus.Unlocked,
                new byte[0],
                null,
                ETag.All,
                0,
                null,
                false,
                null,
                null,
                1000,
                new byte[0],
                null,
                sw.BaseStream,
                DateTimeOffset.Now);

            var response = new Mock<Response>();
            var bdiresponse = Response.FromValue<BlobDownloadInfo>(bdi, null);
            var downloadResponse = new Mock<Response<BlobDownloadInfo>>();
            var usersToUpload = new List<GraphProfileInformation>();

            response.SetupGet(x => x.Status).Returns((int)HttpStatusCode.OK);
            downloadResponse.SetupGet(x => x.Value).Returns(bdiresponse);
            blobClientMock.Setup(x => x.Exists(It.IsAny<CancellationToken>())).Returns(Response.FromValue<bool>(true, new Mock<Response>().Object));
            blobClientMock.Setup(x => x.DownloadAsync()).ReturnsAsync(downloadResponse.Object);
            blobClientMock.Setup(x => x.UploadAsync(It.IsAny<Stream>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                            .Callback((Stream stream, bool overwrite, CancellationToken token) =>
                            {
                                var sr = new StreamReader(stream);

                                using (var reader = new StreamReader(stream))
                                {
                                    reader.ReadLine(); // skip header

                                    string line;
                                    while ((line = reader.ReadLine()) != null)
                                    {
                                        var data = line.Split(",", StringSplitOptions.RemoveEmptyEntries);
                                        if (data.Length >= 2)
                                            usersToUpload.Add(new GraphProfileInformation { PersonnelNumber = data[0], Id = data[1] });
                                    }
                                }
                            });
            blobClientFactoryMock.Setup(x => x.GetBlobClient(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(blobClientMock.Object);

            var users = new List<GraphProfileInformation>
            {
                new GraphProfileInformation{ PersonnelNumber = personnelNumbers[2], Id = Guid.NewGuid().ToString()},
                new GraphProfileInformation{ PersonnelNumber =personnelNumbers[3], Id = Guid.NewGuid().ToString()}
            };

            var service = new AzureUserReaderService(storageAccountSecret, loggerMock.Object, blobClientFactoryMock.Object);
            await service.UploadUsersMemberIdAsync(new Entities.UploadRequest { ContainerName = "mycontainer", BlobTargetDirectory = "/target/folder", Users = users });

            Assert.AreEqual(4, usersToUpload.Count);
            foreach (var personnelNumber in personnelNumbers)
            {
                Assert.IsTrue(usersToUpload.Any(x => x.PersonnelNumber == personnelNumber));
            }
        }

        [TestMethod]
        public async Task UploadUsersMemberIdNoFileTest()
        {
            var storageAccountSecret = new StorageAccountSecret("myconnectionstring");
            var loggerMock = new Mock<ILoggingRepository>();
            var blobClientFactoryMock = new Mock<IBlobClientFactory>();
            var blobClientMock = new Mock<BlobClient>();
            var response = new Mock<Response>();
            var downloadResponse = new Mock<Response<BlobDownloadInfo>>();
            var usersToUpload = new List<GraphProfileInformation>();
            var personnelNumbers = new[] { "111111", "222222" };

            response.SetupGet(x => x.Status).Returns((int)HttpStatusCode.OK);

            blobClientMock.Setup(x => x.Exists(It.IsAny<CancellationToken>())).Returns(Response.FromValue<bool>(false, new Mock<Response>().Object));
            blobClientMock.Setup(x => x.UploadAsync(It.IsAny<Stream>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                            .Callback((Stream stream, bool overwrite, CancellationToken token) =>
                            {
                                var sr = new StreamReader(stream);

                                using (var reader = new StreamReader(stream))
                                {
                                    reader.ReadLine(); // skip header

                                    string line;
                                    while ((line = reader.ReadLine()) != null)
                                    {
                                        var data = line.Split(",", StringSplitOptions.RemoveEmptyEntries);
                                        if (data.Length >= 2)
                                            usersToUpload.Add(new GraphProfileInformation { PersonnelNumber = data[0], Id = data[1] });
                                    }
                                }
                            });
            blobClientFactoryMock.Setup(x => x.GetBlobClient(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(blobClientMock.Object);

            var users = new List<GraphProfileInformation>
            {
                new GraphProfileInformation{ PersonnelNumber = personnelNumbers[0], Id = Guid.NewGuid().ToString()},
                new GraphProfileInformation{ PersonnelNumber =personnelNumbers[1], Id = Guid.NewGuid().ToString()}
            };

            var service = new AzureUserReaderService(storageAccountSecret, loggerMock.Object, blobClientFactoryMock.Object);
            await service.UploadUsersMemberIdAsync(new Entities.UploadRequest { ContainerName = "mycontainer", BlobTargetDirectory = "/target/folder", Users = users });

            Assert.AreEqual(2, usersToUpload.Count);
            foreach (var personnelNumber in personnelNumbers)
            {
                Assert.IsTrue(usersToUpload.Any(x => x.PersonnelNumber == personnelNumber));
            }
        }
    }
}