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
    public partial UserResponse ToResponse(UserModel source);
    public partial List<UserModel> ToModels(List<UserDataModel> source);
    public partial List<UserResponse> ToResponses(List<UserModel> source);
}
