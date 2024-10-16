using IdentityModel;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;

namespace Thea.Auth;

class TheaJwtTokenService : IJwtTokenService
{
    private readonly JwtTokenOptions options;
    public TheaJwtTokenService(IOptions<JwtTokenOptions> options)
        => this.options = options.Value;

    public string CreateToken(UserToken userToken, out DateTime expires)
    {
        var extraClaims = this.BuildClaims(userToken);
        var rsa = RSA.Create();
        byte[] privateKeys = Convert.FromBase64String(this.options.PrivateSecretKey);
        rsa.ImportPkcs8PrivateKey(privateKeys, out _);
        var securityKey = new RsaSecurityKey(rsa);
        var signingCredentials = new SigningCredentials(securityKey, SecurityAlgorithms.RsaSha256);
        expires = DateTime.UtcNow.Add(userToken.LifeTime ?? this.options.LifeTime);
        var securityToken = new JwtSecurityToken(userToken.Issuer ?? this.options.Issuer,
            userToken.Audience ?? this.options.Audience, extraClaims, DateTime.UtcNow, expires, signingCredentials);
        return new JwtSecurityTokenHandler().WriteToken(securityToken);
    }
    public bool ReadToken(string token, out List<Claim> claims)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var securityToken = handler.ReadJwtToken(token);
            claims = securityToken.Claims.ToList();
            return true;
        }
        catch
        {
            claims = null;
            return false;
        }
    }
    protected List<Claim> BuildClaims(UserToken userToken)
    {
        var sessionId = ObjectId.NewId();
        var claims = new List<Claim>()
        {
            new Claim(JwtClaimTypes.Subject, userToken.UserId),
            new Claim(JwtClaimTypes.SessionId, sessionId)
        };
        //可选数据
        if (!string.IsNullOrEmpty(userToken.Account))
            claims.Add(new Claim("acc", userToken.Account));
        if (!string.IsNullOrEmpty(userToken.UserName))
            claims.Add(new Claim(JwtClaimTypes.Name, userToken.UserName));
        if (!string.IsNullOrEmpty(userToken.Email))
            claims.Add(new Claim(JwtClaimTypes.Email, userToken.Email));
        if (!string.IsNullOrEmpty(userToken.Mobile))
            claims.Add(new Claim(JwtClaimTypes.PhoneNumber, userToken.Mobile));
        if (!string.IsNullOrEmpty(userToken.ClientId))
            claims.Add(new Claim(JwtClaimTypes.ClientId, userToken.ClientId));
        if (!string.IsNullOrEmpty(userToken.Scope))
            claims.Add(new Claim(JwtClaimTypes.Scope, userToken.Scope));
        if (!string.IsNullOrEmpty(userToken.Role))
            claims.Add(new Claim(JwtClaimTypes.Role, userToken.Role));
        if (!string.IsNullOrEmpty(userToken.TenantId))
            claims.Add(new Claim("tenant", userToken.TenantId));
        return claims;
    }
}
