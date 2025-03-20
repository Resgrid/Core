require 'bundle/setup'
require 'aesthetic'
require 'aesthetic/example'
require 'rack'

app = Rack.new

Capybara.app = app

example = Aesthetic::Example.new do
  visit '/'
  screenshot :peity
end

example.run

screenshot = Aesthetic.current_screenshots.first # There's only one.

puts screenshot.difference
