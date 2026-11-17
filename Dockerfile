FROM nginx:stable-alpine
ENV PORT=8080
COPY nginx.conf.template /etc/nginx/templates/default.conf.template
COPY *.html *.css *.js *.jpg *.png *.xml *.txt /usr/share/nginx/html/
COPY assets/ /usr/share/nginx/html/assets/
COPY archive/ /usr/share/nginx/html/archive/
COPY logo/ /usr/share/nginx/html/logo/
COPY sons/ /usr/share/nginx/html/sons/
EXPOSE 8080
