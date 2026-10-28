FROM mcr.microsoft.com/playwright:v1.64.1-noble

ENV NODE_ENV=production \
    HOME=/home/pwuser \
    PROFILE_DIR=/data/profile

WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci --omit=dev --ignore-scripts \
    && mkdir -p /data/profile \
    && chown -R pwuser:pwuser /data /app
COPY --chown=pwuser:pwuser src ./src
USER pwuser
EXPOSE 8787
CMD ["xvfb-run", "-a", "--server-args=-screen 0 1280x800x24", "node", "src/server.mjs"]
