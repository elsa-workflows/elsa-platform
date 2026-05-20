# Contract: Deployment Template Bundle API

`POST /api/builder/bundle` and saved configuration bundle routes accept:

```json
{
  "target": "docker-compose"
}
```

Supported first targets:

- `docker-compose`
- `azure-container-apps`
- `kubernetes-helm`

Unsupported targets return error findings and no files.
