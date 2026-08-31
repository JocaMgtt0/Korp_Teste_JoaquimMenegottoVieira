# ---- build ----
FROM node:22-alpine AS build
WORKDIR /app

# Copia apenas os manifestos primeiro: enquanto as dependencias nao mudarem,
# o Docker reaproveita a camada do npm ci e pula a instalacao inteira.
COPY package*.json ./
RUN npm ci

COPY . .
RUN npm run build

# ---- runtime ----
FROM nginx:alpine AS runtime

COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/frontend/browser /usr/share/nginx/html

EXPOSE 80
