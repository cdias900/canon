# The table server, alone in a container.
#
#   docker build -f tools/table-server.Dockerfile -t table-server .
#   docker run --rm -p 8788:8788 -v table-data:/data table-server
#
# Two things about what goes in. Only the server and the one game file it reads are copied — the
# tuning in Assets/Resources/Data/contest.json, at the same relative path, because the server
# resolves it from its own location. NOTHING ELSE under Assets/ is copied, and that is not thrift:
# verses.json carries a licensed translation (NVI, all rights reserved — the reason the repository
# is private), and an image that shipped it would be a copy of it in every registry it touched.
#
# node:sqlite is what the server stores with, so the Node in the image has to be recent enough to
# have it. Pinned, so a rebuild next year is the same server.
FROM node:26-alpine

WORKDIR /srv
COPY tools/table-server.mjs tools/table-admin.mjs tools/
COPY Assets/Resources/Data/contest.json Assets/Resources/Data/contest.json

# The database lives on a volume, not in the image: a container is disposable and a table is not.
VOLUME /data
ENV PORT=8788
ENV TABLE_DB=/data/table.db
# Free text and private tables stay off unless whoever deploys this says otherwise (docs/multiplayer.md §07).
ENV ALLOW_FREE_TEXT=0

EXPOSE 8788
HEALTHCHECK --interval=30s --timeout=3s CMD wget -qO- http://127.0.0.1:${PORT}/health || exit 1
CMD ["sh", "-c", "node tools/table-server.mjs --port ${PORT} --db ${TABLE_DB}"]
