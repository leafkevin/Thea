using IdentityModel;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;

namespace Thea.JwtTokens;

class TheaJwtTokenService : IJwtTokenService
{
    private readonly JwtTokenOptions options;
    public TheaJwtTokenService(IOptions<JwtTokenOptions> options)
        => this.options = options.Value;

    public string CreateToken(UserToken userToken, out List<Claim> claims)
    {
        var extraClaims = this.BuildClaims(userToken);
        var rsa = RSA.Create();
        byte[] privateKeys = Convert.FromBase64String(this.options.SecretKey);
        rsa.ImportPkcs8PrivateKey(privateKeys, out _);
        var securityKey = new RsaSecurityKey(rsa);
        var signingCredentials = new SigningCredentials(securityKey, SecurityAlgorithms.RsaSha256);
        var expires = DateTime.UtcNow.Add(userToken.LifeTime ?? this.options.LifeTime);
        var securityToken = new JwtSecurityToken(this.options.IssuerUri,
            userToken.Audience, extraClaims, DateTime.UtcNow, expires, signingCredentials);
        claims = securityToken.Claims.ToList();
        return new JwtSecurityTokenHandler().WriteToken(securityToken);
    }
    public bool ValidateToken(string token, out List<Claim> claims)
    {
        var rsa = RSA.Create();
        byte[] privateKeys = Convert.FromBase64String(this.options.SecretKey);
        rsa.ImportPkcs8PrivateKey(privateKeys, out _);
        var securityKey = new RsaSecurityKey(rsa);
        var handler = new JwtSecurityTokenHandler();
        var validationParameters = new TokenValidationParameters()
        {
            IssuerSigningKey = securityKey,
            LogTokenId = false,
            LogValidationExceptions = false,
            RequireExpirationTime = false,
            RequireSignedTokens = false,
            RequireAudience = false,
            SaveSigninToken = false,
            TryAllIssuerSigningKeys = true,
            ValidateActor = false,
            ValidateAudience = false,
            ValidateIssuer = false,
            ValidateIssuerSigningKey = false,
            ValidateLifetime = false,
            ValidateTokenReplay = false
        };
        try
        {
            var user = handler.ValidateToken(token, validationParameters, out _);
            claims = user.Claims.ToList();
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
            new Claim(JwtClaimTypes.Subject, userToken.Id),
            new Claim(JwtClaimTypes.SessionId, sessionId),
            new Claim(JwtClaimTypes.Issuer, this.options.IssuerUri),
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
        if (!string.IsNullOrEmpty(userToken.Country))
            claims.Add(new Claim("country", userToken.Country));
        if (!string.IsNullOrEmpty(userToken.State))
            claims.Add(new Claim("state", userToken.State));
        if (!string.IsNullOrEmpty(userToken.City))
            claims.Add(new Claim("city", userToken.City));
        if (userToken.Status.HasValue)
            claims.Add(new Claim("status", userToken.Status.ToString()));
        return claims;
    }
}
