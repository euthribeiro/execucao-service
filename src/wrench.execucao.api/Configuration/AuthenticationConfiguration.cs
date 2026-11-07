using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace wrench.execucao.api.Configuration;

public static class AuthenticationConfiguration
{
    private const string SectionName = "JwtOptions";

    public static IServiceCollection ConfigureAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var secret = configuration.GetSection(SectionName).GetValue<string>("Secret");
        var issuer = configuration.GetSection(SectionName).GetValue<string>("Issuer");
        var audience = configuration.GetSection(SectionName).GetValue<string>("Audience");

        if (string.IsNullOrWhiteSpace(secret) ||
            string.IsNullOrWhiteSpace(issuer) ||
            string.IsNullOrWhiteSpace(audience))
            throw new ArgumentNullException(SectionName, "JWT não configurado");

        var key = Encoding.ASCII.GetBytes(secret);

        services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.RequireHttpsMetadata = false;
                options.SaveToken = true;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ValidIssuer = issuer,
                    ValidAudience = audience,
                    RequireExpirationTime = true,
                    ValidateIssuer = true,
                    ValidateAudience = true
                };
            });

        return services;
    }
}
