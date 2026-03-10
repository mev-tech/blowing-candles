# Feature: Portainer Container Management

## Summary

Add Portainer CE as a container management UI to the Docker Compose stack. Portainer provides a web-based dashboard for monitoring and managing the running containers (app, postgres, and portainer itself). All services in the stack must use `restart: always` to ensure they remain running after crashes or host reboots.

## Acceptance Criteria

- [ ] Portainer CE service added to `docker-compose.yml`
- [ ] Portainer UI accessible via HTTPS on port 9443
- [ ] Portainer data persisted across container restarts via named volume
- [ ] Docker socket mounted read-only for container visibility
- [ ] `restart: always` set on portainer, app, and postgres services
- [ ] Existing app and postgres services unchanged in behavior

## Inputs

| Input | Source | Format | Required |
|-------|--------|--------|----------|
| Existing `docker-compose.yml` | Repository root | Docker Compose v3 | Yes |

## Outputs

| Output | Format | Destination |
|--------|--------|-------------|
| Updated `docker-compose.yml` | Docker Compose | Repository root |

## Configuration

### docker-compose.yml — portainer service

```yaml
services:
  portainer:
    image: portainer/portainer-ce:lts
    restart: always
    ports:
      - "9443:9443"
    volumes:
      - /var/run/docker.sock:/var/run/docker.sock:ro
      - portainer_data:/data

volumes:
  portainer_data:
```

### docker-compose.yml — restart policy additions

```yaml
services:
  app:
    restart: always
    # ... existing config unchanged

  postgres:
    restart: always
    # ... existing config unchanged
```

## Domain Rules

- Portainer must use the `lts` tag for stability (long-term support releases).
- Docker socket is mounted read-only (`:ro`) — Portainer can observe but the principle of least privilege is applied at the mount level.
- Portainer data volume is separate from the postgres `pgdata` volume.
- No authentication is pre-configured — Portainer prompts for admin account creation on first access.

## Error Handling

- If Docker socket is unavailable, Portainer will start but show no environments. This is a host configuration issue, not an application error.
- Portainer CE enforces a 5-minute timeout on the initial admin account creation page. If the container runs longer than 5 minutes before first access, Portainer locks itself. Recovery: restart the container (`docker compose restart portainer`) or reset its data (`docker volume rm <project>_portainer_data`) and navigate to `https://localhost:9443` immediately.
- `restart: always` ensures all services recover from crashes or OOM kills automatically.

## Dependencies

- Docker socket (`/var/run/docker.sock`) must exist on the host.
- No dependencies on other application services — portainer is independent.

## Test Scenarios

| Scenario | Input | Expected Output |
|----------|-------|-----------------|
| Portainer starts | `docker compose up` | Portainer container running, port 9443 accessible |
| Portainer UI loads | Browse to `https://localhost:9443` | Admin setup page on first run |
| Portainer sees all containers | Navigate to container list | app, postgres, portainer all visible |
| Portainer survives restart | `docker restart <portainer-container>` | UI available again, data preserved |
| App survives crash | Kill app container process | Container auto-restarts via `restart: always` |
| Postgres survives crash | Kill postgres container process | Container auto-restarts via `restart: always` |
| Host reboot | Reboot host with Docker set to start on boot | All three services start automatically |

## Implementation Notes

- This is a docker-compose-only change. No Dockerfile, C#, or entrypoint modifications needed.
- The portainer service has no `depends_on` — it is fully independent of the application stack.
- Port 9443 is the default HTTPS port for Portainer CE. Port 8000 (Edge agent) is not exposed as it is not needed for a single-host setup.
