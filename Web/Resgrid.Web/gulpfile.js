/// <binding BeforeBuild='min' Clean='clean' />
"use strict";

var gulp = require("gulp"),
		rimraf = require("rimraf"),
		concat = require("gulp-concat"),
		cssmin = require("gulp-cssmin"),
		uglify = require("gulp-uglify"),
		sass = require("gulp-sass");

var paths = {
	webroot: "./wwwroot/"
};

paths.js = paths.webroot + "js/**/*.js";
paths.minJs = paths.webroot + "js/**/*.min.js";
paths.appJs = paths.webroot + "js/app/**/*.js";
paths.scss = paths.webroot + "scss/style.scss";
paths.siteCss = paths.webroot + "css/style.css";
paths.concatJsDest = paths.webroot + "js/site.min.js";
paths.concatCssDest = paths.webroot + "css/style.min.css";

gulp.task("clean:js", function (cb) {
	rimraf(paths.concatJsDest, cb);
});

gulp.task("clean:css", function (cb) {
	rimraf(paths.concatCssDest, cb);
});

gulp.task("clean", ["clean:js", "clean:css"]);

gulp.task("min:js", function () {
	return gulp.src([paths.js, "!" + paths.minJs, "!" + paths.appJs], { base: "." })
			.pipe(concat(paths.concatJsDest))
			.pipe(uglify())
			.pipe(gulp.dest("."));
});

gulp.task("min:css", function () {
	return gulp.src([paths.siteCss], { base: "." })
			.pipe(cssmin())
			.pipe(concat(paths.concatCssDest))
			.pipe(gulp.dest("."));
});

gulp.task('sass', function () {
	gulp.src('wwwroot/scss/*.scss')
			.pipe(sass().on('error', sass.logError))
			.pipe(gulp.dest('wwwroot/css'));
});

gulp.task('js', function () {
	gulp.src([paths.js, "!" + paths.minJs], { base: "." })
		.pipe(concat(paths.concatJsDest))
		.pipe(gulp.dest("."));
});

gulp.task('sass:watch', function () {
	gulp.watch('wwwroot/scss/**/*.scss', ['sass']);
});

gulp.task("min", ["min:css", "min:js", "sass"]);
