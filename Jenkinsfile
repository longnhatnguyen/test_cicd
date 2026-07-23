pipeline {
    agent any

    environment {
        APP_NAME = 'tinymvcdemo-app'
        APP_IMAGE = 'tinymvcdemo:latest'
        APP_PORT = '5055'
    }

    options {
        timestamps()
    }

    stages {
        stage('Checkout') {
            steps {
                checkout scm
                sh 'git rev-parse --short HEAD > .git/short_commit'
            }
        }

        stage('Build MVC App') {
            steps {
                sh '''
                    docker run --rm \
                      -v "$WORKSPACE/TinyMvcDemo:/src" \
                      -w /src \
                      mcr.microsoft.com/dotnet/sdk:9.0 \
                      dotnet build -c Release
                '''
            }
        }

        stage('Build Docker Image') {
            steps {
                script {
                    env.SHORT_COMMIT = sh(script: "cat .git/short_commit", returnStdout: true).trim()
                    env.DEPLOYED_AT = sh(script: "date '+%Y-%m-%d %H:%M:%S %z'", returnStdout: true).trim()
                }
                sh '''
                    docker build \
                      -t "$APP_IMAGE" \
                      -f TinyMvcDemo/Dockerfile \
                      TinyMvcDemo
                '''
            }
        }

        stage('Deploy') {
            steps {
                sh '''
                    docker rm -f "$APP_NAME" || true
                    docker run -d \
                      --name "$APP_NAME" \
                      -p "$APP_PORT:8080" \
                      -e ASPNETCORE_ENVIRONMENT=Production \
                      -e BUILD_NUMBER="$BUILD_NUMBER" \
                      -e GIT_COMMIT_SHORT="$SHORT_COMMIT" \
                      -e DEPLOYED_AT="$DEPLOYED_AT" \
                      -e DEMO_MESSAGE="Ban dang xem ban duoc deploy tu commit $SHORT_COMMIT." \
                      "$APP_IMAGE"
                '''
            }
        }
    }

    post {
        success {
            echo "Demo app live at http://localhost:${APP_PORT}"
        }
    }
}
