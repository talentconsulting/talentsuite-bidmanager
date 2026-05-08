using Riok.Mapperly.Abstractions;
using TalentSuite.Server.Users.Services.DataModels;
using TalentSuite.Server.Users.Services.Models;
using TalentSuite.Shared.Users;

namespace TalentSuite.Server.Users.Mappers;

[Mapper]
public partial class UserMapper
{
    public partial UserModel ToModel(UserDataModel source);
    public partial UserDataModel ToDataModel(UserModel source);

    public UserResponse ToResponse(UserModel source)
    {
        var response = MapToResponse(source);
        if (source.HasAcceptedRegistration)
            response.InvitationToken = string.Empty;
        return response;
    }

    private partial UserResponse MapToResponse(UserModel source);
    public partial List<UserModel> ToModels(List<UserDataModel> source);

    public List<UserResponse> ToResponses(List<UserModel> source) =>
        source.Select(ToResponse).ToList();
}
