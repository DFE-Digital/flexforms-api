# Changelog

All notable changes to this service will be documented in this file.


## [2.0.0] � Public Beta
### Notes
- FlexForms (Forms Engine SaaS)


---------------------------------------------------------------------------

## [1.0.0] � Public Beta
### Notes
- First formally versioned public beta release.

## [1.0.1] � Public Beta
### Notes
- Optimised Application entity queries and added indexes to the tables.

## [1.0.2] � Public Beta
### Notes
- Added diagnostics endpoint.

## [1.0.3] � Public Beta
### Notes
- Improved loading of application response performance.
- Improved file upload performance.

## [1.1.0] � Public Beta
### Notes
- Added multi-tenancy support.

## [1.1.1] � Public Beta
### Notes
- Improved Auto-Registration to support multi-tenancy and multi-template users 

## [1.1.2] � Public Beta
### Notes
- Improved Unit and Integration tests 

## [1.1.3] � Public Beta
### Notes
- Enabled Test Auth in LSRP Test Environment. 

## [1.1.4] � Public Beta
### Notes
- Updated CoreLibs Security package to support ExtIdentityValidator for multi-tenancy.

## [1.2.0] � Public Beta
### Notes
- Upgraded to .NET10

## [1.2.1] � Public Beta
### Notes
- Updated application status update logic to not update the status if the application is already submitted.

## [1.2.2] � Public Beta
### Notes
- Updated LSRP appsettings for Production environment.

## [1.2.3] � Public Beta
### Notes
- Disabled Test Auth in LSRP Test Environment.

## [1.2.4] � Public Beta
### Notes
- Fixed EmailTemplateResolver logic to support Multi-Tenancy.

## [1.2.5] � Public Beta
### Notes
- Updated LSRP template IDs for govuk notify

## [1.2.6] � Public Beta
### Notes
- Added support for EntraSSO validation and authentication

## [1.2.7] - Public Beta
### Notes
- Added server-side pagination to GET /v1/me/applications (optional pageNumber and pageSize query parameters)

## [1.2.8] - Public Beta
### Notes
- Added RG Visits Test env appsettings

## [1.3.0] - Public Beta
### Notes
- Added new Role assignment endpoint
- New endpoint for retrieving tenant-wide applications
- Added RG Visits Test env appsettings

## [1.3.1] - Public Beta
### Notes
- Added the ability to search for applications by reference on the dashboard

## [1.3.2] - Public Beta
### Notes
- Improved GetApplicationByReference query performance

## [1.3.3] - Public Beta
### Notes
- - Added RG Visits Prod env appsettings

## [1.3.4] - Public Beta
### Notes
- Add optional status filter to get applications by template ID endpoint

## [1.3.5] - Public Beta
### Notes
- Invalidate cache and permissions after a contributor is removed.

## [1.3.6] - Public Beta
### Notes
- Improved caching and invalidate cache and permissions after a change is made.

## [1.3.7] - Public Beta
### Notes
- Added the ability to search for applications by different parameters on the dashboard

## [1.3.8] - Public Beta
### Notes
- Fixed contributor access issues

## [1.4.0] - Public Beta
### Notes
- Added Tests to the PR and Prod workflows

## [1.4.1] - Public Beta
### Notes
- Fix file upload extensions list user message

## [1.5.0] - Public Beta
### Notes
- Added support for Custom Application Status 

## [1.5.1] - Public Beta
### Notes
- Added a new request model for Custom Statuses and fixed model bugs 

## [1.5.2] - Filter on status fix
### Notes
- Fix for the filtering not working when a null value for status is encountered in the db
