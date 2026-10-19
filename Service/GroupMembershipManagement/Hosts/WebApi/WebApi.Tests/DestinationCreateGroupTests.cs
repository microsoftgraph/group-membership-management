// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Models;
using Moq;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using WebApi.Controllers.v1.Destination;
using WebApi.Models.DTOs;

namespace WebApi.Tests
{
    [TestClass]
    public class DestinationCreateGroupTests
    {
        [TestMethod]
        [DataRow("{\"groupName\":\"Finance\",\"groupAlias\":\"finance\"}", null, NewGroupType.Microsoft365)]
        [DataRow("{\"groupName\":\"Finance\",\"groupAlias\":\"finance\",\"description\":null,\"groupType\":1}", null, NewGroupType.Security)]
        [DataRow("{\"groupName\":\"Finance\",\"groupAlias\":\"finance\",\"description\":\"\",\"groupType\":0}", "", NewGroupType.Microsoft365)]
        [DataRow("{\"groupName\":\"Finance\",\"groupAlias\":\"finance\",\"description\":\" \",\"groupType\":1}", " ", NewGroupType.Security)]
        [DataRow("{\"groupName\":\"Finance\",\"groupAlias\":\"finance\",\"description\":\"Project documentation\",\"groupType\":1}", "Project documentation", NewGroupType.Security)]
        public async Task BindsAndForwardsDescriptionWithAuthenticatedOwner(string json, string expectedDescription, NewGroupType expectedType)
        {
            var dto = JsonSerializer.Deserialize<NewGroup>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Assert.IsNotNull(dto);
            Assert.AreEqual(expectedDescription, dto.Description);
            var ownerId = Guid.NewGuid();
            var groupId = Guid.NewGuid();
            dto.UserIdentity = Guid.NewGuid();
            var handler = new Mock<IRequestHandler<PostGroupRequest, PostGroupResponse>>();
            handler.Setup(h => h.ExecuteAsync(It.Is<PostGroupRequest>(r =>
                r.GroupName == "Finance" && r.GroupAlias == "finance" &&
                r.Description == expectedDescription && r.GroupType == expectedType && r.UserIdentity == ownerId)))
                .ReturnsAsync(new PostGroupResponse { GroupId = groupId, StatusCode = HttpStatusCode.OK });
            var controller = new DestinationController(
                Mock.Of<IRequestHandler<SearchGroupsRequest, SearchGroupsResponse>>(),
                Mock.Of<IRequestHandler<SearchChannelsRequest, SearchChannelsResponse>>(),
                Mock.Of<IRequestHandler<GetGroupEndpointsRequest, GetGroupEndpointsResponse>>(),
                Mock.Of<IRequestHandler<GetGroupOwnersRequest, GetGroupOwnersResponse>>(),
                Mock.Of<IRequestHandler<GetGroupOnboardingStatusRequest, GetOnboardingStatusResponse>>(),
                Mock.Of<IRequestHandler<GetChannelOnboardingStatusRequest, GetOnboardingStatusResponse>>(),
                handler.Object, NullLogger<DestinationController>.Instance)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                        {
                            new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", ownerId.ToString())
                        }, "Test"))
                    }
                }
            };

            var result = await controller.CreateGroupAsync(dto);

            Assert.IsInstanceOfType(result.Result, typeof(OkObjectResult));
            Assert.AreEqual(groupId, ((PostGroupResponse)((OkObjectResult)result.Result).Value).GroupId);
            handler.VerifyAll();
        }

        [TestMethod]
        public void BackendContractDoesNotExposePreviewFields()
        {
            CollectionAssert.AreEquivalent(
                new[] { "UserIdentity", "GroupName", "GroupAlias", "GroupType", "Description" },
                typeof(NewGroup).GetProperties().Select(p => p.Name).ToArray());
        }
    }
}
