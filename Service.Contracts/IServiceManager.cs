﻿namespace Service.Contracts;

public interface IServiceManager
{
    IAppImageService AppImageService { get; }
    IAuthorizationService AuthorizationService { get; }
}