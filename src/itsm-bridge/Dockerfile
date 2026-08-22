# Self-contained build context (src/itsm-bridge itself, not the repo root) — unlike the .NET
# services, this Java project has no cross-project references to reach outside its own folder for.

FROM eclipse-temurin:21-jdk AS build
WORKDIR /src

# Wrapper + pom first, restore dependencies, then the rest of the source — same Docker-layer-cache
# reasoning as every other Dockerfile here: don't repeat a full dependency download on every code
# change.
COPY mvnw pom.xml ./
COPY .mvn/ .mvn/
RUN ./mvnw dependency:go-offline -B

COPY src/ src/
RUN ./mvnw package -DskipTests -B

FROM eclipse-temurin:21-jre AS runtime
WORKDIR /app
COPY --from=build /src/target/itsm-bridge-*.jar app.jar

EXPOSE 8080
ENTRYPOINT ["java", "-jar", "app.jar"]
