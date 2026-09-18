# Build stage
FROM node:20-alpine AS build
WORKDIR /app
COPY package*.json ./
RUN npm ci
COPY . .
# We must build with the production URL of the API.
# In a real-world scenario, this would be an environment variable.
# For local docker-compose, the user will access the site via localhost, 
# and the backend will be mapped to localhost:8080.
ENV VITE_API_BASE_URL=http://localhost:8080
RUN npm run build

# Serve stage
FROM nginx:alpine
COPY --from=build /app/dist /usr/share/nginx/html
# Add SPA routing configuration
RUN echo 'server { \
    listen       80; \
    server_name  localhost; \
    location / { \
        root   /usr/share/nginx/html; \
        index  index.html index.htm; \
        try_files $uri $uri/ /index.html; \
    } \
    error_page   500 502 503 504  /50x.html; \
    location = /50x.html { \
        root   /usr/share/nginx/html; \
    } \
}' > /etc/nginx/conf.d/default.conf

EXPOSE 80
CMD ["nginx", "-g", "daemon off;"]
