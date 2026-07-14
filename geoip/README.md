# Local GeoIP database

The application performs IP geolocation locally and never sends visitor IP
addresses to a third-party geolocation API.

To enable country and city fields:

1. Create a free MaxMind GeoLite account.
2. Download the binary `GeoLite2-City` database.
3. Extract `GeoLite2-City.mmdb` into this directory.
4. Restart the web container or application.

The database file is intentionally ignored by Git and the Docker build context.
Keep it current with MaxMind's `geoipupdate` utility. GeoLite's license requires
outdated database releases to be removed after an updated release becomes
available. VPS update automation will be configured during deployment.
