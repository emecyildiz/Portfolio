# Production privacy notice

The public privacy notice reads its operator and hosting details from the
production environment. Keep these values in `/etc/emecworks/portfolio.env`;
do not hard-code personal contact details in the repository.

Required public settings:

```dotenv
PRIVACY_CONTROLLER_NAME=Public operator name
PRIVACY_CONTACT_EMAIL=privacy@example.com
PRIVACY_HOSTING_PROVIDER=Hosting provider
PRIVACY_HOSTING_COUNTRY=Hosting country
```

The values are passed to ASP.NET Core by `docker-compose.prod.yml`. If any
value is empty, `/privacy` deliberately displays a launch-configuration
warning.

After changing the environment file, recreate only the web container:

```bash
cd /opt/emecworks/portfolio
sudo docker compose --env-file /etc/emecworks/portfolio.env \
  -f docker-compose.prod.yml up -d --no-deps --force-recreate web
```

Then verify that the warning and placeholder text are absent:

```bash
curl -fsS https://emecworks.com/privacy |
  grep -E "Launch configuration is incomplete|To be configured|to be configured"
```

`grep` should produce no output. Finally, read the rendered page and confirm
that every public detail is accurate.
