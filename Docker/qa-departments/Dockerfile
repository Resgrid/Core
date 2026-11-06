# syntax=docker/dockerfile:1.7
# Resgrid QA departments seeder: isolated, ad hoc test departments for the app regression suites.
# Build context: this folder.  docker build -t resgrid-qa/departments:local Docker/qa-departments
# Run: see README.md (seed needs SEED_DB_* and --allow-database; the manifest with credentials lands in /out).
FROM python:3.13-slim
ENV PYTHONDONTWRITEBYTECODE=1 PYTHONUNBUFFERED=1
WORKDIR /app
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt
COPY seeder ./seeder
COPY catalog ./catalog
COPY tests ./tests
# The unit tests gate the image: a catalog or generator change that breaks them never ships.
RUN python -m unittest discover -s tests -t . -q
RUN useradd --create-home --uid 10001 seeder && mkdir /out && chown seeder /out
USER seeder
VOLUME ["/out"]
ENTRYPOINT ["python", "-m", "seeder"]
CMD ["plan"]
