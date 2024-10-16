using System;
using System.Collections.Generic;
using System.Security.Claims;

namespace Thea.Auth;

public interface IJwtTokenService
{
    string CreateToken(UserToken userToken, out DateTime expires);
    bool ReadToken(string token, out List<Claim> claims);
}